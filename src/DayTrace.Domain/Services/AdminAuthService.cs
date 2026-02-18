using System.Security.Cryptography;
using System.Text;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Handles admin authentication: login with email+password, session creation,
/// and audit logging. Per US-051 / FR-12.4.
/// </summary>
public class AdminAuthService
{
    private readonly IAdminUserRepository _adminUserRepo;
    private readonly IAdminSessionRepository _adminSessionRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly IDomainLogger _logger;

    public AdminAuthService(
        IAdminUserRepository adminUserRepo,
        IAdminSessionRepository adminSessionRepo,
        IAuditLogRepository auditLogRepo,
        IDomainLogger logger)
    {
        _adminUserRepo = adminUserRepo;
        _adminSessionRepo = adminSessionRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates admin by email+password, returns session token.
    /// </summary>
    public async Task<AdminLoginResult> LoginAsync(string email, string password)
    {
        var admin = await _adminUserRepo.GetByEmailAsync(email.Trim().ToLowerInvariant());

        if (admin == null || admin.Status != "active")
        {
            await LogAuditAsync("admin", null, "login", "admin_user", null, "failure");
            return AdminLoginResult.Failure("Invalid credentials");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            await LogAuditAsync("admin", admin.Id.ToString(), "login", "admin_user", admin.Id.ToString(), "failure");
            return AdminLoginResult.Failure("Invalid credentials");
        }

        // Create session
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenHash = ComputeSha256(token);
        var session = new AdminSession
        {
            AdminUserId = admin.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            CreatedAt = DateTime.UtcNow
        };

        await _adminSessionRepo.CreateAsync(session);
        await LogAuditAsync("admin", admin.Id.ToString(), "login", "admin_user", admin.Id.ToString(), "success");

        _logger.Info("Admin login successful for {Email}", admin.Email);

        return AdminLoginResult.Success(token, admin);
    }

    /// <summary>
    /// Validates admin session token, returns admin user if valid.
    /// No sliding window — 8h fixed TTL.
    /// </summary>
    public async Task<AdminSession?> ValidateSessionAsync(string token)
    {
        var tokenHash = ComputeSha256(token);
        var session = await _adminSessionRepo.GetByTokenHashAsync(tokenHash);

        if (session == null || session.ExpiresAt <= DateTime.UtcNow)
            return null;

        return session;
    }

    /// <summary>
    /// Logs out admin by deleting session.
    /// </summary>
    public async Task LogoutAsync(string token, long adminUserId)
    {
        var tokenHash = ComputeSha256(token);
        await _adminSessionRepo.DeleteByTokenHashAsync(tokenHash);
        await LogAuditAsync("admin", adminUserId.ToString(), "logout", "admin_user", adminUserId.ToString(), "success");
    }

    /// <summary>
    /// Seeds initial admin user (idempotent).
    /// </summary>
    public async Task<AdminUser> SeedAdminAsync(string email, string password, string role = "admin")
    {
        var existing = await _adminUserRepo.GetByEmailAsync(email.Trim().ToLowerInvariant());
        if (existing != null)
        {
            _logger.Info("Admin user already exists: {Email}", email);
            return existing;
        }

        var admin = new AdminUser
        {
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        await _adminUserRepo.CreateAsync(admin);
        await LogAuditAsync("system", null, "seed_admin", "admin_user", admin.Id.ToString(), "success");

        _logger.Info("Admin user seeded: {Email} with role {Role}", email, role);
        return admin;
    }

    private async Task LogAuditAsync(string actorType, string? actorId, string action, string? targetType, string? targetId, string outcome)
    {
        var log = new AuditLog
        {
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = outcome,
            CreatedAt = DateTime.UtcNow
        };
        await _auditLogRepo.CreateAsync(log);
    }

    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public class AdminLoginResult
{
    public bool IsSuccess { get; private set; }
    public string? Token { get; private set; }
    public string? ErrorMessage { get; private set; }
    public AdminUser? Admin { get; private set; }

    public static AdminLoginResult Success(string token, AdminUser admin)
        => new() { IsSuccess = true, Token = token, Admin = admin };

    public static AdminLoginResult Failure(string message)
        => new() { IsSuccess = false, ErrorMessage = message };
}
