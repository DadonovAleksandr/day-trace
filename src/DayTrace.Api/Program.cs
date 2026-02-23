using DayTrace.Api;
using DayTrace.Api.BackgroundServices;
using DayTrace.Api.Middleware;
using DayTrace.Bot;
using DayTrace.Infrastructure;
using DayTrace.Infrastructure.Data;
using Microsoft.Extensions.FileProviders;
using NLog;
using NLog.Web;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Starting DayTrace API");

    var builder = WebApplication.CreateBuilder(args);

    // NLog
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Infrastructure (EF Core + PostgreSQL + Domain services)
    builder.Services.AddInfrastructure(builder.Configuration);

    // Telegram Bot
    builder.Services.AddTelegramBot(builder.Configuration);

    // Health checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<DayTraceDbContext>("postgresql");

    // CORS
    var allowedOrigins = builder.Configuration.GetValue<string>("ALLOWED_ORIGINS") ?? "*";
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (allowedOrigins == "*")
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
        });
    });

    // Background services (bot-dependent — only register when bot token is configured)
    var botToken = builder.Configuration.GetSection("TelegramBot").GetValue<string>("BotToken");
    if (!string.IsNullOrEmpty(botToken))
    {
        builder.Services.AddHostedService<BotWebhookSetupService>();
    }
    builder.Services.AddHostedService<OperationIdCleanupService>();
    builder.Services.AddHostedService<PeriodJobWorkerService>();
    builder.Services.AddHostedService<StuckJobReaperService>();
    builder.Services.AddHostedService<DailyReminderService>();
    builder.Services.AddHostedService<DeliveryRetryService>();
    builder.Services.AddHostedService<UserPurgeService>();
    builder.Services.AddHostedService<AuditLogCleanupService>();

    // Controllers + Swagger — snake_case JSON to match frontend contract
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
            o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "DayTrace API", Version = "v1" });
    });

    var app = builder.Build();

    // Middleware pipeline
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DayTrace API v1"));
    }

    app.UseCors();

    // Serve miniapp static files (from src/miniapp/dist)
    var miniappDistPath = Path.Combine(app.Environment.ContentRootPath, "..", "miniapp", "dist");
    if (Directory.Exists(miniappDistPath))
    {
        var fileProvider = new PhysicalFileProvider(Path.GetFullPath(miniappDistPath));
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            OnPrepareResponse = ctx =>
            {
                // Hashed assets (JS/CSS with content hash in filename) — cache long
                if (ctx.File.Name.Contains('.') && ctx.Context.Request.Path.StartsWithSegments("/assets"))
                {
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }
                else
                {
                    // index.html and other root files — always revalidate
                    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers.Pragma = "no-cache";
                    ctx.Context.Response.Headers.Expires = "0";
                }
            }
        });
    }

    app.UseMiddleware<SessionAuthMiddleware>();
    app.UseMiddleware<AdminAuthMiddleware>();
    app.UseMiddleware<ClientOperationIdMiddleware>();
    app.MapControllers();
    app.MapHealthChecks("/health/db");
    app.MapGet("/privacy", () => Results.Content(PrivacyPage.Html, "text/html; charset=utf-8"));

    // SPA fallback: unmatched routes → miniapp index.html
    if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "..", "miniapp", "dist")))
    {
        var distFullPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "miniapp", "dist"));
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(distFullPath)
        });
    }

    // Seed admin user from env vars if configured (US-053)
    await app.SeedAdminUserAsync();
    await app.SeedWisdomsAsync();

    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Application stopped due to exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}

// Needed for WebApplicationFactory<Program> in integration tests
public partial class Program { }
