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

public class DeliveryRetryServiceTests
{
    private readonly Mock<IDeliveryAttemptRepository> _deliveryRepoMock;
    private readonly Mock<IAdminBroadcastCampaignRepository> _broadcastRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<ISummaryRepository> _summaryRepoMock;
    private readonly Mock<ITelegramBotClient> _botClientMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly DeliveryRetryService _service;

    private const long TestUserId = 42;
    private const long TestTelegramUserId = 123456789;

    public DeliveryRetryServiceTests()
    {
        _deliveryRepoMock = new Mock<IDeliveryAttemptRepository>();
        _broadcastRepoMock = new Mock<IAdminBroadcastCampaignRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _summaryRepoMock = new Mock<ISummaryRepository>();
        _botClientMock = new Mock<ITelegramBotClient>();

        _serviceProviderMock = new Mock<IServiceProvider>();
        _scopeMock = new Mock<IServiceScope>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IDeliveryAttemptRepository)))
            .Returns(_deliveryRepoMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IAdminBroadcastCampaignRepository)))
            .Returns(_broadcastRepoMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IUserRepository)))
            .Returns(_userRepoMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ISummaryRepository)))
            .Returns(_summaryRepoMock.Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ITelegramBotClient)))
            .Returns(_botClientMock.Object);

        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);

        var botOptions = Options.Create(new TelegramBotOptions
        {
            MiniAppUrl = "https://app.example.com",
            WebhookBaseUrl = "https://webhook.example.com"
        });

        var logger = NullLogger<DeliveryRetryService>.Instance;

        _service = new DeliveryRetryService(_scopeFactoryMock.Object, botOptions, logger);
    }

    private static User CreateTestUser(long id = TestUserId, long telegramUserId = TestTelegramUserId)
    {
        return new User
        {
            Id = id,
            TelegramUserId = telegramUserId,
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        };
    }

    private static DeliveryAttempt CreateAttempt(
        string deliveryType = "reminder",
        string status = "failed",
        int attemptNumber = 1,
        long? referenceId = null,
        DateTime? lastAttemptAt = null,
        DateTime? createdAt = null)
    {
        return new DeliveryAttempt
        {
            Id = 1,
            UserId = TestUserId,
            DeliveryType = deliveryType,
            Status = status,
            AttemptNumber = attemptNumber,
            ReferenceId = referenceId,
            LastAttemptAt = lastAttemptAt,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-10)
        };
    }

    private static Message CreateTelegramMessage(int messageId, long chatId)
    {
        var json = $$"""{"message_id":{{messageId}},"chat":{"id":{{chatId}},"type":"private"},"date":0}""";
        return JsonSerializer.Deserialize<Message>(json, JsonBotAPI.Options)!;
    }

    private void SetupSendMessageSuccess(long chatId = TestTelegramUserId, int messageId = 456)
    {
        _botClientMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTelegramMessage(messageId, chatId));
    }

    private void SetupSendMessageThrows(Exception exception)
    {
        _botClientMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    // ========== Test 1: No bot client ==========

    [Fact]
    public async Task ProcessRetries_NoBotClient_Returns()
    {
        // Arrange: GetService<ITelegramBotClient> returns null
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ITelegramBotClient)))
            .Returns(null!);

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: no repository calls made
        _deliveryRepoMock.Verify(
            r => r.GetRetryableAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ========== Test 2: No retryable attempts ==========

    [Fact]
    public async Task ProcessRetries_NoRetryable_Returns()
    {
        // Arrange
        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt>());

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: no update calls
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ========== Test 3: Backoff not ready, skip ==========

    [Fact]
    public async Task ProcessRetries_BackoffNotReady_Skips()
    {
        // Arrange: AttemptNumber=2, LastAttemptAt=now → backoff = 30s * 2^(2-1) = 60s, not elapsed
        var attempt = CreateAttempt(
            attemptNumber: 2,
            lastAttemptAt: DateTime.UtcNow);

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: no update (skipped due to backoff)
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ========== Test 4: Backoff ready, processes ==========

    [Fact]
    public async Task ProcessRetries_BackoffReady_Processes()
    {
        // Arrange: AttemptNumber=1, LastAttemptAt=2 min ago → backoff = 30s * 2^0 = 30s, elapsed
        var attempt = CreateAttempt(
            attemptNumber: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        SetupSendMessageSuccess();

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: update was called (attempt was processed)
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<DeliveryAttempt>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 5: User not found → terminal_failed ==========

    [Fact]
    public async Task ProcessRetries_UserNotFound_TerminalFailed()
    {
        // Arrange
        var attempt = CreateAttempt(
            attemptNumber: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert
        Assert.Equal("terminal_failed", attempt.Status);
        Assert.Equal("User not found", attempt.ErrorMessage);
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(attempt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 6: User has no TelegramUserId → terminal_failed ==========

    [Fact]
    public async Task ProcessRetries_UserNoTelegramId_TerminalFailed()
    {
        // Arrange
        var attempt = CreateAttempt(
            attemptNumber: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser(telegramUserId: 0));

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert
        Assert.Equal("terminal_failed", attempt.Status);
        Assert.Equal("User has no TelegramUserId", attempt.ErrorMessage);
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(attempt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 7: Reminder type → correct text ==========

    [Fact]
    public async Task ProcessRetries_ReminderType_CorrectText()
    {
        // Arrange
        var attempt = CreateAttempt(
            deliveryType: "reminder",
            attemptNumber: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        SetupSendMessageSuccess();

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: verify SendRequest was called with text containing the reminder message
        _botClientMock.Verify(
            b => b.SendRequest<Message>(
                It.Is<IRequest<Message>>(req =>
                    req.ToString()!.Contains("Не забудьте записать события дня") ||
                    VerifySendMessageText(req, "📝 Не забудьте записать события дня! Откройте приложение или отправьте текст боту.")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 8: Soft reminder → resolve period name ==========

    [Fact]
    public async Task ProcessRetries_SoftReminderType_ResolvePeriodName()
    {
        // Arrange
        var summaryId = 100L;
        var attempt = CreateAttempt(
            deliveryType: "soft_reminder",
            attemptNumber: 1,
            referenceId: summaryId,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        _summaryRepoMock
            .Setup(r => r.GetByIdAsync(summaryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Summary
            {
                Id = summaryId,
                UserId = TestUserId,
                PeriodType = "weekly",
                PeriodStart = new DateOnly(2026, 2, 16),
                PeriodEnd = new DateOnly(2026, 2, 22),
                Status = "generated",
                Version = 1
            });

        SetupSendMessageSuccess();

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: SendRequest was called (soft_reminder processed)
        _summaryRepoMock.Verify(
            r => r.GetByIdAsync(summaryId, It.IsAny<CancellationToken>()),
            Times.Once);
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 9: Admin broadcast → uses campaign text ==========

    [Fact]
    public async Task ProcessRetries_AdminBroadcast_UseCampaignText()
    {
        // Arrange
        var campaignId = 50L;
        var campaignText = "Important announcement!";
        var attempt = CreateAttempt(
            deliveryType: "admin_broadcast",
            status: "pending",
            attemptNumber: 1,
            referenceId: campaignId,
            createdAt: DateTime.UtcNow);

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        _broadcastRepoMock
            .Setup(r => r.GetByIdAsync(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminBroadcastCampaign
            {
                Id = campaignId,
                Text = campaignText,
                Status = "processing",
                Audience = "active"
            });

        SetupSendMessageSuccess();

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: campaign was fetched and message was sent
        _broadcastRepoMock.Verify(
            r => r.GetByIdAsync(campaignId, It.IsAny<CancellationToken>()),
            Times.Once);
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 10: Admin broadcast, no campaign → terminal_failed ==========

    [Fact]
    public async Task ProcessRetries_AdminBroadcast_NoCampaign_TerminalFailed()
    {
        // Arrange
        var campaignId = 999L;
        var attempt = CreateAttempt(
            deliveryType: "admin_broadcast",
            status: "pending",
            attemptNumber: 1,
            referenceId: campaignId,
            createdAt: DateTime.UtcNow);

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        _broadcastRepoMock
            .Setup(r => r.GetByIdAsync(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminBroadcastCampaign?)null);

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert
        Assert.Equal("terminal_failed", attempt.Status);
        Assert.Equal("Broadcast campaign not found", attempt.ErrorMessage);
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(attempt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 11: Admin broadcast, no ReferenceId → terminal_failed ==========

    [Fact]
    public async Task ProcessRetries_AdminBroadcast_NoReferenceId_TerminalFailed()
    {
        // Arrange: admin_broadcast with ReferenceId = null
        var attempt = CreateAttempt(
            deliveryType: "admin_broadcast",
            status: "pending",
            attemptNumber: 1,
            referenceId: null,
            createdAt: DateTime.UtcNow);

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert
        Assert.Equal("terminal_failed", attempt.Status);
        Assert.Equal("Broadcast campaign reference is missing", attempt.ErrorMessage);
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(attempt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 12: Send success → status sent ==========

    [Fact]
    public async Task ProcessRetries_SendSuccess_StatusSent()
    {
        // Arrange
        var attempt = CreateAttempt(
            deliveryType: "reminder",
            attemptNumber: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        SetupSendMessageSuccess(messageId: 789);

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert
        Assert.Equal("sent", attempt.Status);
        Assert.NotNull(attempt.SentAt);
        Assert.Equal(789, attempt.TelegramMessageId);
        _deliveryRepoMock.Verify(
            r => r.UpdateAsync(attempt, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Test 13: Transient error → status failed (retryable) ==========

    [Fact]
    public async Task ProcessRetries_TransientError_StatusFailed()
    {
        // Arrange: AttemptNumber=1 (will be incremented to 2, below MaxAttempts=5)
        var attempt = CreateAttempt(
            deliveryType: "reminder",
            attemptNumber: 1,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-2));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        SetupSendMessageThrows(new ApiRequestException("Too Many Requests", 429));

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: transient + below max → "failed" (retryable)
        Assert.Equal("failed", attempt.Status);
        Assert.Contains("Too Many Requests", attempt.ErrorMessage);
        Assert.Equal(2, attempt.AttemptNumber); // incremented from 1 to 2
    }

    // ========== Test 14: Transient error at max attempts → terminal_failed ==========

    [Fact]
    public async Task ProcessRetries_TransientError_MaxAttempts_TerminalFailed()
    {
        // Arrange: AttemptNumber=4 (will be incremented to 5=MaxAttempts → terminal_failed)
        var attempt = CreateAttempt(
            deliveryType: "reminder",
            attemptNumber: 4,
            lastAttemptAt: DateTime.UtcNow.AddMinutes(-30));

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        SetupSendMessageThrows(new ApiRequestException("Too Many Requests", 429));

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: transient + at max → terminal_failed
        Assert.Equal("terminal_failed", attempt.Status);
        Assert.Equal(5, attempt.AttemptNumber); // incremented from 4 to 5
        Assert.Contains("Too Many Requests", attempt.ErrorMessage);
    }

    // ========== Test 15: Pending admin_broadcast skips backoff ==========

    [Fact]
    public async Task ProcessRetries_PendingAdminBroadcast_SkipsBackoff()
    {
        // Arrange: pending admin_broadcast with CreatedAt=just now → normally backoff would block
        var campaignId = 50L;
        var attempt = CreateAttempt(
            deliveryType: "admin_broadcast",
            status: "pending",
            attemptNumber: 1,
            referenceId: campaignId,
            createdAt: DateTime.UtcNow); // just now — backoff would block for non-broadcast
        attempt.LastAttemptAt = null; // no previous attempt

        _deliveryRepoMock
            .Setup(r => r.GetRetryableAsync(5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeliveryAttempt> { attempt });

        _userRepoMock
            .Setup(r => r.GetByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestUser());

        _broadcastRepoMock
            .Setup(r => r.GetByIdAsync(campaignId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminBroadcastCampaign
            {
                Id = campaignId,
                Text = "Broadcast text",
                Status = "processing",
                Audience = "active"
            });

        SetupSendMessageSuccess();

        // Act
        await _service.ProcessRetriesAsync(CancellationToken.None);

        // Assert: message was sent (backoff skipped for pending admin_broadcast)
        Assert.Equal("sent", attempt.Status);
        Assert.NotNull(attempt.SentAt);
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ========== Helper ==========

    /// <summary>
    /// Helper to verify the text in a SendMessage request via reflection.
    /// Since SendMessage is an extension method that creates a request object internally,
    /// we inspect the request's properties.
    /// </summary>
    private static bool VerifySendMessageText(IRequest<Message> request, string expectedText)
    {
        // Try to access the Text property via reflection
        var textProp = request.GetType().GetProperty("Text");
        if (textProp != null)
        {
            var actualText = textProp.GetValue(request) as string;
            return actualText == expectedText;
        }
        return false;
    }
}
