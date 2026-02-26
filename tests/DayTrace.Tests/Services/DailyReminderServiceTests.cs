using System.Text.Json;
using DayTrace.Api.BackgroundServices;
using DayTrace.Bot.Configuration;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using User = DayTrace.Domain.Entities.User;

namespace DayTrace.Tests.Services;

public class DailyReminderServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IDeliveryAttemptRepository> _deliveryRepoMock;
    private readonly Mock<ITelegramBotClient> _botClientMock;
    private readonly ILogger<DailyReminderService> _logger;
    private readonly TelegramBotOptions _botOptions;

    private const long UserId = 42;
    private const long TelegramUserId = 100500;

    public DailyReminderServiceTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _deliveryRepoMock = new Mock<IDeliveryAttemptRepository>();
        _botClientMock = new Mock<ITelegramBotClient>();
        _logger = NullLogger<DailyReminderService>.Instance;
        _botOptions = new TelegramBotOptions
        {
            MiniAppUrl = "https://miniapp.example.com",
            WebhookBaseUrl = "https://webhook.example.com"
        };

        // Default: SendMessage succeeds
        _botClientMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTelegramMessage(123, TelegramUserId));

        // Default: CreateAsync returns the attempt back
        _deliveryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeliveryAttempt a, CancellationToken _) => a);
    }

    private DailyReminderService CreateService(IServiceScopeFactory? scopeFactory = null)
    {
        var options = Options.Create(_botOptions);
        return new DailyReminderService(
            scopeFactory ?? CreateScopeFactory(),
            options,
            _logger);
    }

    private IServiceScopeFactory CreateScopeFactory(ITelegramBotClient? botClient = null)
    {
        var actualBotClient = botClient ?? _botClientMock.Object;

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IUserRepository)))
            .Returns(_userRepoMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IDeliveryAttemptRepository)))
            .Returns(_deliveryRepoMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ITelegramBotClient)))
            .Returns(actualBotClient);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock.Object;
    }

    private IServiceScopeFactory CreateScopeFactoryWithoutBotClient()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IUserRepository)))
            .Returns(_userRepoMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IDeliveryAttemptRepository)))
            .Returns(_deliveryRepoMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ITelegramBotClient)))
            .Returns(null!);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock.Object;
    }

    /// <summary>
    /// Creates a Telegram Message via JSON deserialization to work around init-only properties.
    /// </summary>
    private static Message CreateTelegramMessage(int messageId, long chatId)
    {
        var json = $$"""{"message_id":{{messageId}},"chat":{"id":{{chatId}},"type":"private"},"date":0}""";
        return JsonSerializer.Deserialize<Message>(json, JsonBotAPI.Options)!;
    }

    private static User CreateUser(
        string timezone = "Europe/Moscow",
        TimeOnly? reminderTime = null)
    {
        return new User
        {
            Id = UserId,
            TelegramUserId = TelegramUserId,
            Status = "active",
            Settings = new UserSettings
            {
                UserId = UserId,
                Timezone = timezone,
                ReminderTime = reminderTime ?? new TimeOnly(21, 0),
                ReminderEnabled = true
            }
        };
    }

    // ========== ProcessRemindersAsync ==========

    [Fact]
    public async Task ProcessReminders_NoBotClient_Skips()
    {
        var scopeFactory = CreateScopeFactoryWithoutBotClient();
        var service = CreateService(scopeFactory);

        await service.ProcessRemindersAsync(CancellationToken.None);

        _userRepoMock.Verify(
            r => r.GetActiveUsersWithRemindersAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessReminders_NoUsersWithReminders_NoDelivery()
    {
        _userRepoMock
            .Setup(r => r.GetActiveUsersWithRemindersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());

        var service = CreateService();

        await service.ProcessRemindersAsync(CancellationToken.None);

        _deliveryRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ========== ProcessUserReminderAsync ==========

    [Fact]
    public async Task ProcessUserReminder_InvalidTimezone_Skips()
    {
        var user = CreateUser(timezone: "Invalid/Zone");
        var nowUtc = DateTime.UtcNow;

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        _deliveryRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserReminder_ReminderTimeNotYet_Skips()
    {
        // User in Europe/Moscow (UTC+3), reminder at 21:00 local
        // nowUtc = 17:00 UTC → local = 20:00 MSK → before 21:00, skip
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

        // Pick a date, compute scheduled UTC, then set nowUtc 1 hour before
        var nowUtc = new DateTime(2026, 6, 15, 17, 0, 0, DateTimeKind.Utc); // 20:00 MSK
        // scheduled = 21:00 MSK = 18:00 UTC → nowUtc (17:00) < scheduledUtc (18:00) → skip

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        _deliveryRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserReminder_ReminderTimePassed10Min_Skips()
    {
        // User in Europe/Moscow (UTC+3), reminder at 21:00 local = 18:00 UTC
        // nowUtc = 18:15 UTC → 15 min after scheduledUtc → exceeds 10min window → skip
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        var nowUtc = new DateTime(2026, 6, 15, 18, 15, 0, DateTimeKind.Utc);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        _deliveryRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserReminder_AlreadySentToday_Skips()
    {
        // User in Europe/Moscow (UTC+3), reminder at 21:00 local = 18:00 UTC
        // nowUtc = 18:03 UTC → within window
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        var nowUtc = new DateTime(2026, 6, 15, 18, 3, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        _deliveryRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserReminder_Success_CreatesDeliveryAndSends()
    {
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        // 18:03 UTC = 21:03 MSK → within 10-min window after 21:00 MSK (18:00 UTC)
        var nowUtc = new DateTime(2026, 6, 15, 18, 3, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Capture the status at CreateAsync time (before it gets mutated to "sent")
        string? statusAtCreation = null;
        _deliveryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => statusAtCreation = a.Status)
            .ReturnsAsync((DeliveryAttempt a, CancellationToken _) => a);

        DeliveryAttempt? capturedAttempt = null;
        _deliveryRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => capturedAttempt = a)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        // Verify delivery attempt was created with pending status
        _deliveryRepoMock.Verify(
            r => r.CreateAsync(
                It.Is<DeliveryAttempt>(a =>
                    a.UserId == UserId &&
                    a.DeliveryType == "reminder" &&
                    a.AttemptNumber == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("pending", statusAtCreation);

        // Verify Telegram message was sent
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify delivery attempt was updated to sent
        Assert.NotNull(capturedAttempt);
        Assert.Equal("sent", capturedAttempt!.Status);
        Assert.NotNull(capturedAttempt.SentAt);
        Assert.Equal(123, capturedAttempt.TelegramMessageId);
    }

    [Fact]
    public async Task ProcessUserReminder_TelegramTransientError_StatusFailed()
    {
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        var nowUtc = new DateTime(2026, 6, 15, 18, 3, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // SendMessage throws 429 Too Many Requests
        _botClientMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Too Many Requests", 429));

        DeliveryAttempt? capturedAttempt = null;
        _deliveryRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => capturedAttempt = a)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        Assert.NotNull(capturedAttempt);
        Assert.Equal("failed", capturedAttempt!.Status);
        Assert.NotNull(capturedAttempt.ErrorMessage);
    }

    [Fact]
    public async Task ProcessUserReminder_TelegramTerminalError_StatusTerminalFailed()
    {
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        var nowUtc = new DateTime(2026, 6, 15, 18, 3, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // SendMessage throws 403 Forbidden (bot blocked by user)
        _botClientMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        DeliveryAttempt? capturedAttempt = null;
        _deliveryRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => capturedAttempt = a)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        Assert.NotNull(capturedAttempt);
        Assert.Equal("terminal_failed", capturedAttempt!.Status);
        Assert.Contains("blocked", capturedAttempt.ErrorMessage!);
    }

    [Fact]
    public async Task ProcessUserReminder_DstSpringForward_AdjustsTime()
    {
        // America/New_York: DST spring-forward on 2026-03-08 at 2:00 AM → 3:00 AM
        // Reminder at 2:30 AM local → invalid time → should shift to 3:30 AM local
        // 3:30 AM EDT (UTC-4) = 07:30 UTC
        var user = CreateUser(timezone: "America/New_York", reminderTime: new TimeOnly(2, 30));

        // nowUtc = 07:31 UTC (just after 3:31 AM EDT, within 10-min window of shifted 3:30 AM)
        var nowUtc = new DateTime(2026, 3, 8, 7, 31, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DeliveryAttempt? capturedAttempt = null;
        _deliveryRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => capturedAttempt = a)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        // Should have created and sent the delivery
        _deliveryRepoMock.Verify(
            r => r.CreateAsync(
                It.Is<DeliveryAttempt>(a => a.DeliveryType == "reminder"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(capturedAttempt);
        Assert.Equal("sent", capturedAttempt!.Status);

        // Verify scheduledAt was set to the adjusted time (3:30 AM EDT = 07:30 UTC)
        Assert.NotNull(capturedAttempt.ScheduledAt);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc),
            capturedAttempt.ScheduledAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProcessUserReminder_DstFallBack_UsesFirstOccurrence()
    {
        // America/New_York: DST fall-back on 2026-11-01 at 2:00 AM → 1:00 AM
        // Reminder at 1:30 AM local → ambiguous time (occurs twice)
        // First occurrence: 1:30 AM EDT (UTC-4) = 05:30 UTC
        // Second occurrence: 1:30 AM EST (UTC-5) = 06:30 UTC
        // Service should use first occurrence (maxOffset = -4h → 05:30 UTC)
        var user = CreateUser(timezone: "America/New_York", reminderTime: new TimeOnly(1, 30));

        // nowUtc = 05:31 UTC → just after first occurrence of 1:30 AM, within 10-min window
        var nowUtc = new DateTime(2026, 11, 1, 5, 31, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DeliveryAttempt? capturedAttempt = null;
        _deliveryRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => capturedAttempt = a)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        _deliveryRepoMock.Verify(
            r => r.CreateAsync(
                It.Is<DeliveryAttempt>(a => a.DeliveryType == "reminder"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(capturedAttempt);
        Assert.Equal("sent", capturedAttempt!.Status);

        // Verify scheduledAt = first occurrence: 1:30 AM EDT (UTC-4) = 05:30 UTC
        Assert.NotNull(capturedAttempt.ScheduledAt);
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc),
            capturedAttempt.ScheduledAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProcessUserReminder_WithinWindow_SendsReminder()
    {
        // User in Europe/Moscow (UTC+3), reminder at 21:00 local = 18:00 UTC
        // nowUtc = 18:05 UTC → 5 min after scheduledUtc → within 10-min window → sends
        var user = CreateUser(timezone: "Europe/Moscow", reminderTime: new TimeOnly(21, 0));
        var nowUtc = new DateTime(2026, 6, 15, 18, 5, 0, DateTimeKind.Utc);

        _deliveryRepoMock
            .Setup(r => r.HasReminderForDateAsync(
                UserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DeliveryAttempt? capturedAttempt = null;
        _deliveryRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()))
            .Callback<DeliveryAttempt, CancellationToken>((a, _) => capturedAttempt = a)
            .Returns(Task.CompletedTask);

        var service = CreateService();

        await service.ProcessUserReminderAsync(
            user, nowUtc, _botClientMock.Object, _deliveryRepoMock.Object, CancellationToken.None);

        // Verify message was sent
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(capturedAttempt);
        Assert.Equal("sent", capturedAttempt!.Status);
        Assert.Equal(123, capturedAttempt.TelegramMessageId);
    }
}
