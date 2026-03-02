using DayTrace.Bot.Configuration;
using DayTrace.Bot.Handlers;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = DayTrace.Domain.Entities.User;

namespace DayTrace.Tests.Services;

public class BotUpdateHandlerTests
{
    private readonly Mock<ITelegramBotClient> _botClientMock;
    private readonly Mock<IUserFeedbackRepository> _feedbackRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IUserSettingsRepository> _settingsRepoMock;
    private readonly Mock<IWeekScheduleHistoryRepository> _weekScheduleRepoMock;
    private readonly Mock<ITimezoneHistoryRepository> _timezoneHistoryRepoMock;
    private readonly Mock<IDomainLogger> _domainLoggerMock;
    private readonly TelegramBotOptions _options;
    private readonly BotUpdateHandler _handler;

    private const long TestTelegramUserId = 12345;
    private const long TestChatId = 67890;
    private const long TestUserId = 1;

    public BotUpdateHandlerTests()
    {
        _botClientMock = new Mock<ITelegramBotClient>();
        _feedbackRepoMock = new Mock<IUserFeedbackRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _settingsRepoMock = new Mock<IUserSettingsRepository>();
        _weekScheduleRepoMock = new Mock<IWeekScheduleHistoryRepository>();
        _timezoneHistoryRepoMock = new Mock<ITimezoneHistoryRepository>();
        _domainLoggerMock = new Mock<IDomainLogger>();

        _options = new TelegramBotOptions
        {
            BotToken = "test-token",
            WebhookBaseUrl = "https://test.example.com",
            MiniAppUrl = "https://miniapp.example.com",
            WebhookSecretToken = "secret"
        };

        var optionsMock = new Mock<IOptions<TelegramBotOptions>>();
        optionsMock.Setup(o => o.Value).Returns(_options);

        // Default: UserRepository returns null (new user path)
        _userRepoMock
            .Setup(r => r.GetByTelegramUserIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) =>
            {
                u.Id = TestUserId;
                return u;
            });

        _settingsRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<UserSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSettings s, CancellationToken _) => s);

        _weekScheduleRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<WeekScheduleHistory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WeekScheduleHistory w, CancellationToken _) => w);

        _timezoneHistoryRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<TimezoneHistory>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimezoneHistory t, CancellationToken _) => t);

        _feedbackRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<UserFeedback>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserFeedback f, CancellationToken _) =>
            {
                f.Id = 100;
                return f;
            });

        // Mock SendMessage (extension method delegates to SendRequest<Message>)
        SetupSendMessage();

        // Mock AnswerCallbackQuery (extension method delegates to SendRequest<bool>)
        SetupAnswerCallbackQuery();

        var registrationService = new UserRegistrationService(
            _userRepoMock.Object,
            _settingsRepoMock.Object,
            _weekScheduleRepoMock.Object,
            _timezoneHistoryRepoMock.Object,
            _domainLoggerMock.Object);

        var subscriptionRepoMock = new Mock<ISubscriptionRepository>();
        var starPaymentRepoMock = new Mock<IStarPaymentRepository>();
        var subscriptionService = new SubscriptionService(
            subscriptionRepoMock.Object,
            starPaymentRepoMock.Object);

        _handler = new BotUpdateHandler(
            _botClientMock.Object,
            optionsMock.Object,
            registrationService,
            _settingsRepoMock.Object,
            _feedbackRepoMock.Object,
            subscriptionService,
            NullLogger<BotUpdateHandler>.Instance);
    }

    #region Helpers

    private void SetupSendMessage()
    {
        _botClientMock
            .Setup(b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponseMessage());
    }

    private void SetupAnswerCallbackQuery()
    {
        _botClientMock
            .Setup(b => b.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static Message CreateResponseMessage()
    {
        // Message.MessageId is readonly in Telegram.Bot 22.9.0; create via default and set Chat only
        var msg = new Message { Chat = new Chat { Id = TestChatId }, Date = DateTime.UtcNow };
        return msg;
    }

    private void SetupExistingUser(User user)
    {
        _userRepoMock
            .Setup(r => r.GetByTelegramUserIdAsync(user.TelegramUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
    }

    private static Update CreateMessageUpdate(string text, long? telegramUserId = null, long? chatId = null,
        bool nullFrom = false)
    {
        var from = nullFrom
            ? null
            : new Telegram.Bot.Types.User
            {
                Id = telegramUserId ?? TestTelegramUserId,
                FirstName = "Test"
            };

        return new Update
        {
            Id = 1,
            Message = new Message
            {
                Chat = new Chat { Id = chatId ?? TestChatId, Type = ChatType.Private },
                From = from,
                Text = text,
                Date = DateTime.UtcNow
            }
        };
    }

    private static Update CreateCallbackQueryUpdate(string data, long? chatId = null, long? fromUserId = null,
        string? callbackQueryId = null)
    {
        var cid = chatId ?? TestChatId;
        return new Update
        {
            Id = 1,
            CallbackQuery = new CallbackQuery
            {
                Id = callbackQueryId ?? Guid.NewGuid().ToString(),
                From = new Telegram.Bot.Types.User
                {
                    Id = fromUserId ?? TestTelegramUserId,
                    FirstName = "Test"
                },
                Message = new Message
                {
                    Chat = new Chat { Id = cid, Type = ChatType.Private },
                    From = new Telegram.Bot.Types.User { Id = 999, IsBot = true, FirstName = "Bot" },
                    Date = DateTime.UtcNow
                },
                Data = data
            }
        };
    }

    private static User CreateTestUser(bool isUtcTimezone = false, long telegramUserId = TestTelegramUserId)
    {
        return new User
        {
            Id = TestUserId,
            TelegramUserId = telegramUserId,
            CreatedAt = DateTime.UtcNow,
            Status = "active",
            Settings = new UserSettings
            {
                UserId = TestUserId,
                Timezone = isUtcTimezone ? "UTC" : "Europe/Moscow",
                ReminderTime = new TimeOnly(21, 0),
                ReminderEnabled = true,
                WeekEnd = "Sunday"
            }
        };
    }

    /// <summary>
    /// Extracts SendMessageRequest objects from bot client invocations to inspect sent text.
    /// </summary>
    private List<SendMessageRequest> GetSendMessageRequests()
    {
        return _botClientMock.Invocations
            .Where(i => i.Method.Name == nameof(ITelegramBotClient.SendRequest)
                        && i.Arguments.Count > 0
                        && i.Arguments[0] is SendMessageRequest)
            .Select(i => (SendMessageRequest)i.Arguments[0])
            .ToList();
    }

    #endregion

    // 1. /start — new user -> sends "Добро пожаловать"
    [Fact]
    public async Task HandleUpdate_StartCommand_NewUser_SendsWelcome()
    {
        // Arrange: default setup = no existing user
        var update = CreateMessageUpdate("/start");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("Добро пожаловать", requests[0].Text);
    }

    // 2. /start — existing user -> sends "С возвращением"
    [Fact]
    public async Task HandleUpdate_StartCommand_ExistingUser_SendsWelcomeBack()
    {
        // Arrange
        var existingUser = CreateTestUser();
        SetupExistingUser(existingUser);
        var update = CreateMessageUpdate("/start");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("С возвращением", requests[0].Text);
    }

    // 3. /start — user with UTC timezone -> shows timezone hint
    [Fact]
    public async Task HandleUpdate_StartCommand_UtcTimezone_ShowsTimezoneHint()
    {
        // Arrange
        var utcUser = CreateTestUser(isUtcTimezone: true);
        SetupExistingUser(utcUser);
        var update = CreateMessageUpdate("/start");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("часовой пояс", requests[0].Text);
    }

    // 4. /help -> sends help text with "Как пользоваться"
    [Fact]
    public async Task HandleUpdate_HelpCommand_SendsHelpText()
    {
        // Arrange
        var update = CreateMessageUpdate("/help");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("Как пользоваться", requests[0].Text);
    }

    // 5. Unrecognized text -> saves as feedback
    [Fact]
    public async Task HandleUpdate_UnrecognizedText_SavesFeedback()
    {
        // Arrange
        var update = CreateMessageUpdate("Отличный сервис");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        _feedbackRepoMock.Verify(
            r => r.CreateAsync(
                It.Is<UserFeedback>(f => f.Text == "Отличный сервис" && f.Status == "new"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("Спасибо за обратную связь", requests[0].Text);
    }

    // 6. Long text (3000 chars) -> feedback text is truncated to 2000
    [Fact]
    public async Task HandleUpdate_UnrecognizedText_LongText_Truncates()
    {
        // Arrange
        var longText = new string('A', 3000);
        var update = CreateMessageUpdate(longText);

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        _feedbackRepoMock.Verify(
            r => r.CreateAsync(
                It.Is<UserFeedback>(f => f.Text.Length == 2000),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // 7. Feedback save throws -> sends error message
    [Fact]
    public async Task HandleUpdate_UnrecognizedText_Error_SendsErrorMessage()
    {
        // Arrange: existing user so registration succeeds before feedback fails
        var existingUser = CreateTestUser();
        SetupExistingUser(existingUser);

        _feedbackRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<UserFeedback>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB connection failed"));

        var update = CreateMessageUpdate("Some feedback text");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("Не удалось сохранить", requests[0].Text);
    }

    // 8. Empty/whitespace text -> nothing happens (no feedback saved)
    [Fact]
    public async Task HandleUpdate_EmptyText_Ignores()
    {
        // Arrange
        var update = CreateMessageUpdate("   ");

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert
        _feedbackRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<UserFeedback>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 9. message.From = null -> no exception, no SendMessage
    [Fact]
    public async Task HandleUpdate_NullFrom_Ignores()
    {
        // Arrange
        var update = CreateMessageUpdate("/start", nullFrom: true);

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert: no messages sent
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 10. Callback with "summary_" prefix -> sends redirect message
    [Fact]
    public async Task HandleUpdate_CallbackQuery_SummaryPrefix_SendsRedirect()
    {
        // Arrange: unique chatId to avoid dedup collisions
        var uniqueChatId = 100010L;
        var update = CreateCallbackQueryUpdate("summary_weekly", chatId: uniqueChatId);

        // Act
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // Assert: AnswerCallbackQuery was called
        _botClientMock.Verify(
            b => b.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: SendMessage with redirect text
        var requests = GetSendMessageRequests();
        Assert.Single(requests);
        Assert.Contains("Выберите главное событие", requests[0].Text);
    }

    // 11. Callback dedup: two calls within 3s -> second only answers, no SendMessage
    [Fact]
    public async Task HandleUpdate_CallbackQuery_DedupWithin3s_Ignores()
    {
        // Arrange: unique chatId/data to isolate from other tests
        var uniqueChatId = 200011L;
        var callbackData = "dedup_test_11";

        var update1 = CreateCallbackQueryUpdate(callbackData, chatId: uniqueChatId, callbackQueryId: "cb1_11");
        var update2 = CreateCallbackQueryUpdate(callbackData, chatId: uniqueChatId, callbackQueryId: "cb2_11");

        // Act: first call
        await _handler.HandleUpdateAsync(update1, CancellationToken.None);

        // Reset invocations to count only the second call
        _botClientMock.Invocations.Clear();

        // Act: second call within 3s -> should be deduped
        await _handler.HandleUpdateAsync(update2, CancellationToken.None);

        // Assert: AnswerCallbackQuery still called (dedup path)
        _botClientMock.Verify(
            b => b.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: no SendMessage -> handler logic was skipped
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 12. Callback after >3s -> processes normally (not deduped)
    [Fact]
    public async Task HandleUpdate_CallbackQuery_After3s_Processes()
    {
        // Arrange: unique chatId/data to isolate
        var uniqueChatId = 300012L;
        var callbackData = "summary_month_test_12";

        var update1 = CreateCallbackQueryUpdate(callbackData, chatId: uniqueChatId, callbackQueryId: "cb_first_12");

        // Act: first call
        await _handler.HandleUpdateAsync(update1, CancellationToken.None);

        // Verify first call sent a redirect message
        var firstRequests = GetSendMessageRequests();
        Assert.Single(firstRequests);

        // Simulate >3s elapsed: set dedup entry timestamp to 4 seconds ago via reflection
        var dedupeKey = $"{uniqueChatId}_{callbackData}";
        var recentCallbacksField = typeof(BotUpdateHandler)
            .GetField("RecentCallbacks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var recentCallbacks =
            (System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>)recentCallbacksField!.GetValue(null)!;
        recentCallbacks[dedupeKey] = DateTime.UtcNow.AddSeconds(-4);

        // Reset invocations
        _botClientMock.Invocations.Clear();

        var update2 = CreateCallbackQueryUpdate(callbackData, chatId: uniqueChatId, callbackQueryId: "cb_second_12");

        // Act: second call after >3s
        await _handler.HandleUpdateAsync(update2, CancellationToken.None);

        // Assert: AnswerCallbackQuery called
        _botClientMock.Verify(
            b => b.SendRequest<bool>(It.IsAny<IRequest<bool>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: SendMessage called (not deduped)
        var secondRequests = GetSendMessageRequests();
        Assert.Single(secondRequests);
        Assert.Contains("Выберите главное событие", secondRequests[0].Text);
    }

    // 13. Unknown update type (no Message, no CallbackQuery) -> no exception
    [Fact]
    public async Task HandleUpdate_UnknownUpdateType_NoException()
    {
        // Arrange
        var update = new Update { Id = 999 };

        // Act & Assert: should not throw
        await _handler.HandleUpdateAsync(update, CancellationToken.None);

        // No SendMessage called
        _botClientMock.Verify(
            b => b.SendRequest<Message>(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
