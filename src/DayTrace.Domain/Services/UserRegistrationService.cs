using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// User registration domain service (FR-0).
/// Pure domain service — bot UX and Mini App UX call this service.
/// </summary>
public class UserRegistrationService
{
    private readonly IUserRepository _userRepo;
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly IWeekScheduleHistoryRepository _weekScheduleRepo;
    private readonly ITimezoneHistoryRepository _timezoneHistoryRepo;
    private readonly IDomainLogger _logger;

    public UserRegistrationService(
        IUserRepository userRepo,
        IUserSettingsRepository settingsRepo,
        IWeekScheduleHistoryRepository weekScheduleRepo,
        ITimezoneHistoryRepository timezoneHistoryRepo,
        IDomainLogger logger)
    {
        _userRepo = userRepo;
        _settingsRepo = settingsRepo;
        _weekScheduleRepo = weekScheduleRepo;
        _timezoneHistoryRepo = timezoneHistoryRepo;
        _logger = logger;
    }

    /// <summary>
    /// Registers a new user or returns existing one.
    /// Idempotent by telegram_user_id.
    /// </summary>
    /// <param name="telegramUserId">Telegram user ID</param>
    /// <param name="detectedTimezone">Detected timezone from Mini App (optional)</param>
    /// <returns>Tuple of (User, isNew flag)</returns>
    public async Task<(User User, bool IsNew)> RegisterAsync(
        long telegramUserId,
        string? detectedTimezone = null,
        CancellationToken ct = default)
    {
        // Check for existing user (idempotent upsert)
        var existingUser = await _userRepo.GetByTelegramUserIdAsync(telegramUserId, ct);
        if (existingUser != null)
        {
            // Block auth for soft-deleted/purged users
            if (existingUser.Status != "active")
            {
                throw new AuthenticationException("account_deleted",
                    "This account has been deleted and cannot be accessed");
            }

            _logger.Info("User already exists for telegram_user_id={TelegramUserId}, user_id={UserId}",
                telegramUserId, existingUser.Id);
            return (existingUser, false);
        }

        // Determine timezone
        var timezone = "UTC";
        if (!string.IsNullOrWhiteSpace(detectedTimezone) && DateCalculationService.IsValidTimezone(detectedTimezone))
        {
            timezone = detectedTimezone;
        }

        // Create user (idempotent: handle concurrent registration race on unique telegram_user_id)
        User user;
        try
        {
            user = new User
            {
                TelegramUserId = telegramUserId,
                CreatedAt = DateTime.UtcNow,
                Status = "active"
            };
            user = await _userRepo.CreateAsync(user, ct);
        }
        catch (Exception)
        {
            // Unique constraint violation — concurrent insert won, re-read
            var raceWinner = await _userRepo.GetByTelegramUserIdAsync(telegramUserId, ct);
            if (raceWinner == null)
                throw; // Not a unique violation, rethrow original

            if (raceWinner.Status != "active")
                throw new AuthenticationException("account_deleted",
                    "This account has been deleted and cannot be accessed");
            return (raceWinner, false);
        }

        _logger.Info("New user registered: telegram_user_id={TelegramUserId}, user_id={UserId}, timezone={Timezone}",
            telegramUserId, user.Id, timezone);

        // Create default settings (FR-0)
        var settings = new UserSettings
        {
            UserId = user.Id,
            Timezone = timezone,
            ReminderTime = new TimeOnly(21, 0), // default 21:00
            ReminderEnabled = true,
            WeekEnd = "Sunday"
        };
        await _settingsRepo.CreateAsync(settings, ct);

        // Create initial timezone_history record (FR-0, FR-2.2)
        var timezoneHistory = new TimezoneHistory
        {
            UserId = user.Id,
            Timezone = timezone,
            EffectiveFrom = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await _timezoneHistoryRepo.CreateAsync(timezoneHistory, ct);

        // Create initial week_schedule_history record with
        // effective_from = registration_date - 31 days (FR-0)
        var registrationDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekSchedule = new WeekScheduleHistory
        {
            UserId = user.Id,
            WeekEnd = "Sunday",
            EffectiveFromLocalDate = registrationDate.AddDays(-31),
            CreatedAt = DateTime.UtcNow
        };
        await _weekScheduleRepo.CreateAsync(weekSchedule, ct);

        user.Settings = settings;

        return (user, true);
    }
}
