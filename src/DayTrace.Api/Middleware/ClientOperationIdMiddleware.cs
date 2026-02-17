using System.Text.Json;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Api.Middleware;

/// <summary>
/// Deduplicates requests by X-Client-Operation-Id header (FR-9, section 4.1).
/// Applies only to POST, PATCH, DELETE endpoints; GET requests bypass dedupe.
/// Missing header on POST/PATCH/DELETE → 400.
/// </summary>
public class ClientOperationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClientOperationIdMiddleware> _logger;

    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PATCH", "DELETE"
    };

    // Paths that don't require X-Client-Operation-Id (e.g., auth endpoints)
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/telegram",
        "/bot/webhook",
        "/health/db",
    };

    public ClientOperationIdMiddleware(RequestDelegate next, ILogger<ClientOperationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";

        // Only apply to mutating methods
        if (!MutatingMethods.Contains(method))
        {
            await _next(context);
            return;
        }

        // Skip exempt paths
        if (IsExemptPath(path))
        {
            await _next(context);
            return;
        }

        // Check for X-Client-Operation-Id header
        if (!context.Request.Headers.TryGetValue("X-Client-Operation-Id", out var operationIdValues)
            || string.IsNullOrWhiteSpace(operationIdValues.FirstOrDefault()))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "validation_error",
                message = "X-Client-Operation-Id header is required for POST/PATCH/DELETE requests"
            });
            return;
        }

        var clientOperationId = operationIdValues.First()!;

        // User must be authenticated for dedupe to work (need user_id)
        if (!context.Items.TryGetValue("UserId", out var userIdObj) || userIdObj is not long userId)
        {
            // No user context — skip dedupe (auth middleware will handle 401)
            await _next(context);
            return;
        }

        var route = path;
        var repo = context.RequestServices.GetRequiredService<IOperationIdCacheRepository>();

        // Check for existing operation
        var existing = await repo.GetAsync(userId, method, route, clientOperationId);
        if (existing != null && !string.IsNullOrEmpty(existing.ResponseHash))
        {
            // Duplicate within 5 min TTL → return cached response
            if ((DateTime.UtcNow - existing.CreatedAt).TotalMinutes <= 5)
            {
                _logger.LogInformation(
                    "Dedupe: returning cached response for operation_id={OperationId}, user_id={UserId}",
                    clientOperationId, userId);

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(existing.ResponseHash);
                return;
            }
        }

        // Capture response for caching
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        await _next(context);

        // Read the response body
        memStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(memStream).ReadToEndAsync();

        // Cache only successful responses (2xx)
        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            var entry = new Domain.Entities.OperationIdCache
            {
                UserId = userId,
                Method = method,
                Route = route,
                ClientOperationId = clientOperationId,
                ResponseHash = responseBody,
                CreatedAt = DateTime.UtcNow
            };
            await repo.TryInsertAsync(entry);
        }

        // Write the response back to the original stream
        memStream.Seek(0, SeekOrigin.Begin);
        await memStream.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    }

    private static bool IsExemptPath(string path)
    {
        foreach (var exempt in ExemptPaths)
        {
            if (path.StartsWith(exempt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
