using System.Security.Cryptography;
using System.Text;
using System.Web;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Handles Telegram Mini App authentication (FR-12.1).
/// Validates init data HMAC, checks auth_date, creates sessions.
/// </summary>
public class TelegramAuthService
{
    private readonly ISessionRepository _sessionRepo;
    private readonly UserRegistrationService _registrationService;
    private readonly IDomainLogger _logger;

    public TelegramAuthService(
        ISessionRepository sessionRepo,
        UserRegistrationService registrationService,
        IDomainLogger logger)
    {
        _sessionRepo = sessionRepo;
        _registrationService = registrationService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a Telegram Mini App user via init data.
    /// Returns (token, user, isNew) on success, or throws on failure.
    /// </summary>
    public async Task<AuthResult> AuthenticateAsync(
        string initDataRaw,
        string botToken,
        string? detectedTimezone = null,
        CancellationToken ct = default)
    {
        // Parse init data
        var parameters = ParseInitData(initDataRaw);

        if (!parameters.TryGetValue("hash", out var receivedHash))
            throw new AuthenticationException("missing_hash", "Hash parameter is missing from init data");

        // Check auth_date FIRST (before replay protection per AC)
        if (!parameters.TryGetValue("auth_date", out var authDateStr)
            || !long.TryParse(authDateStr, out var authDateUnix))
            throw new AuthenticationException("invalid_auth_date", "auth_date is missing or invalid");

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix).UtcDateTime;
        var age = DateTime.UtcNow - authDate;
        if (age.TotalSeconds > 300)
            throw new AuthenticationException("init_data_expired", "Init data has expired (older than 300 seconds)");

        // Validate HMAC signature
        if (!ValidateHmac(parameters, receivedHash, botToken))
            throw new AuthenticationException("invalid_signature", "HMAC signature validation failed");

        // Extract user info from init data
        var telegramUserId = ExtractTelegramUserId(parameters);
        if (telegramUserId == 0)
            throw new AuthenticationException("invalid_user", "Cannot extract user from init data");

        // Register or get existing user
        var (user, isNew) = await _registrationService.RegisterAsync(telegramUserId, detectedTimezone, ct);

        // Create session token
        var token = Guid.NewGuid().ToString("N");
        var tokenHash = ComputeSha256(token);

        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };
        await _sessionRepo.CreateAsync(session, ct);

        _logger.Info("Telegram auth successful: user_id={UserId}, is_new={IsNew}, session_id={SessionId}",
            user.Id, isNew, session.Id);

        return new AuthResult
        {
            Token = token,
            User = user,
            IsNew = isNew
        };
    }

    /// <summary>
    /// Parses URL-encoded init data into a dictionary.
    /// </summary>
    public static Dictionary<string, string> ParseInitData(string initDataRaw)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(initDataRaw))
            return result;

        var pairs = initDataRaw.Split('&');
        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0) continue;
            var key = HttpUtility.UrlDecode(pair[..eqIndex]);
            var value = HttpUtility.UrlDecode(pair[(eqIndex + 1)..]);
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Validates the HMAC-SHA256 signature of Telegram init data.
    /// Per Telegram docs: 
    ///   secret_key = HMAC-SHA256("WebAppData", bot_token)
    ///   data_check_string = sorted key=value pairs (excluding hash), joined by \n
    ///   expected_hash = HMAC-SHA256(secret_key, data_check_string)
    /// </summary>
    public static bool ValidateHmac(Dictionary<string, string> parameters, string receivedHash, string botToken)
    {
        // Build data_check_string: sort params alphabetically (excluding "hash"), join with \n
        var dataCheckString = string.Join('\n',
            parameters
                .Where(kv => kv.Key != "hash")
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}={kv.Value}"));

        // secret_key = HMAC-SHA256("WebAppData", bot_token)
        using var hmacKey = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = hmacKey.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        // hash = HMAC-SHA256(secret_key, data_check_string)
        using var hmacData = new HMACSHA256(secretKey);
        var hashBytes = hmacData.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
        var computedHash = Convert.ToHexStringLower(hashBytes);

        return string.Equals(computedHash, receivedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts telegram_user_id from the "user" JSON field in init data.
    /// </summary>
    private static long ExtractTelegramUserId(Dictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("user", out var userJson))
            return 0;

        try
        {
            // Simple JSON parsing for {"id":123456,...}
            // Find "id": followed by a number
            var idIndex = userJson.IndexOf("\"id\"", StringComparison.Ordinal);
            if (idIndex < 0) return 0;

            var colonIndex = userJson.IndexOf(':', idIndex + 4);
            if (colonIndex < 0) return 0;

            var start = colonIndex + 1;
            while (start < userJson.Length && (userJson[start] == ' ' || userJson[start] == '\t'))
                start++;

            var end = start;
            while (end < userJson.Length && char.IsDigit(userJson[end]))
                end++;

            if (end == start) return 0;
            return long.TryParse(userJson[start..end], out var id) ? id : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Computes SHA256 hash of input string and returns hex string.
    /// </summary>
    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}

public class AuthResult
{
    public string Token { get; set; } = string.Empty;
    public User? User { get; set; }
    public bool IsNew { get; set; }
}

public class AuthenticationException : Exception
{
    public string ErrorCode { get; }

    public AuthenticationException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
