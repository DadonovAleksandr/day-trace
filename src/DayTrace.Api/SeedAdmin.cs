using DayTrace.Domain.Services;

namespace DayTrace.Api;

/// <summary>
/// Admin seed: creates initial admin user on startup from environment variables.
/// Per US-053 / FR-12.4.
/// 
/// Environment variables:
///   ADMIN_SEED_EMAIL — email for initial admin (required to trigger seed)
///   ADMIN_SEED_PASSWORD — password for initial admin (required)
/// 
/// Idempotent: re-running doesn't create duplicates.
/// </summary>
public static class AdminSeedExtensions
{
    public static async Task SeedAdminUserAsync(this WebApplication app)
    {
        var email = app.Configuration["ADMIN_SEED_EMAIL"];
        var password = app.Configuration["ADMIN_SEED_PASSWORD"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return; // No seed requested
        }

        using var scope = app.Services.CreateScope();
        var adminAuthService = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AdminAuthService>>();

        try
        {
            var admin = await adminAuthService.SeedAdminAsync(email, password, "admin");
            logger.LogInformation("Admin seed complete: {Email} (ID: {Id})", admin.Email, admin.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed admin user: {Email}", email);
        }
    }
}
