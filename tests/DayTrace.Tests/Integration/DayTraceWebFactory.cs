using System.Security.Cryptography;
using System.Text;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Services;
using DayTrace.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory that replaces the DB connection with the test container.
/// </summary>
public class DayTraceWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public DayTraceWebFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<DayTraceDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Add test DB context
            services.AddDbContext<DayTraceDbContext>(options =>
            {
                options.UseNpgsql(_connectionString);
            });
        });
    }

    /// <summary>
    /// Creates a test user with session token and returns (userId, rawToken).
    /// </summary>
    public async Task<(long UserId, string Token)> CreateTestUserAsync(
        string timezone = "UTC",
        string weekEnd = "Sunday")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();

        var user = new User
        {
            TelegramUserId = Random.Shared.NextInt64(100000, 999999999),
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var settings = new UserSettings
        {
            UserId = user.Id,
            Timezone = timezone,
            ReminderTime = new TimeOnly(21, 0),
            ReminderEnabled = true,
            WeekEnd = weekEnd
        };
        db.UsersSettings.Add(settings);

        var weekSchedule = new WeekScheduleHistory
        {
            UserId = user.Id,
            WeekEnd = weekEnd,
            EffectiveFromLocalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-31),
            CreatedAt = DateTime.UtcNow
        };
        db.WeekScheduleHistory.Add(weekSchedule);

        var timezoneHistory = new TimezoneHistory
        {
            UserId = user.Id,
            Timezone = timezone,
            EffectiveFrom = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.TimezoneHistory.Add(timezoneHistory);

        // Create session
        var rawToken = Guid.NewGuid().ToString();
        var tokenHash = TelegramAuthService.ComputeSha256(rawToken);
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };
        db.UserSessions.Add(session);

        await db.SaveChangesAsync();

        return (user.Id, rawToken);
    }

    /// <summary>
    /// Creates an HttpClient with auth token pre-configured.
    /// </summary>
    public async Task<(HttpClient Client, long UserId)> CreateAuthenticatedClientAsync(
        string timezone = "UTC",
        string weekEnd = "Sunday")
    {
        var (userId, token) = await CreateTestUserAsync(timezone, weekEnd);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    /// <summary>
    /// Creates an admin user with session token and returns (rawToken, adminId).
    /// </summary>
    public async Task<(string Token, long AdminId)> CreateAdminUserAsync(string role = "admin")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();

        var email = $"admin-{Guid.NewGuid():N}@test.local";
        var password = "TestPassword123!";
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

        var admin = new AdminUser
        {
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.AdminUsers.Add(admin);
        await db.SaveChangesAsync();

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenHash = ComputeSha256(rawToken);

        var session = new AdminSession
        {
            AdminUserId = admin.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            CreatedAt = DateTime.UtcNow
        };
        db.AdminSessions.Add(session);
        await db.SaveChangesAsync();

        return (rawToken, admin.Id);
    }

    /// <summary>
    /// Creates an admin user with known credentials for login tests.
    /// Returns (email, password, adminId).
    /// </summary>
    public async Task<(string Email, string Password, long AdminId)> CreateAdminUserWithCredentialsAsync(string role = "admin")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();

        var email = $"admin-{Guid.NewGuid():N}@test.local";
        var password = "TestPassword123!";
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

        var admin = new AdminUser
        {
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.AdminUsers.Add(admin);
        await db.SaveChangesAsync();

        return (email, password, admin.Id);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Cleans all data from tables for test isolation.
    /// </summary>
    public async Task CleanDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();

        // Delete in FK-safe order
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM operation_id_cache;
            DELETE FROM auth_replay_cache;
            DELETE FROM prompt_deliveries;
            DELETE FROM delivery_attempts;
            DELETE FROM period_jobs;
            DELETE FROM period_run_counters;
            DELETE FROM summaries;
            DELETE FROM events;
            DELETE FROM week_schedule_history;
            DELETE FROM timezone_history;
            DELETE FROM users_settings;
            DELETE FROM user_sessions;
            DELETE FROM admin_sessions;
            DELETE FROM audit_logs;
            DELETE FROM admin_users;
            DELETE FROM users;
        ");
    }
}
