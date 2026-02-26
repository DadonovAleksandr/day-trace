# План добавления тестов: Background Services + Bot Handler

> **Статус: ЗАВЕРШЁН** — все 40 тестов реализованы и проходят.

## Обзор

Добавление unit-тестов с мокированием зависимостей (Moq) для:
- **Background Services**: DailyReminderService, DeliveryRetryService
- **Bot Handler**: BotUpdateHandler

Стек: xUnit + Moq (как в существующих `EventLockServiceTests`).

> Background services тестируются через извлечение бизнес-логики в тестируемые методы, а не через запуск ExecuteAsync (polling loop). Bot handler тестируется напрямую через `HandleUpdateAsync`.

---

## Фаза 1: DailyReminderService (unit-тесты) ✅

**Файл**: `tests/DayTrace.Tests/Services/DailyReminderServiceTests.cs`

**Изменения в production-коде**:
- `DailyReminderService`: `private` → `internal` для `ProcessRemindersAsync` и `ProcessUserReminderAsync`
- `DayTrace.Api.csproj`: добавлен `<InternalsVisibleTo Include="DayTrace.Tests" />`

### Тесты (12 тестов):

- [x] **1.1** `ProcessReminders_NoBotClient_Skips` — если `ITelegramBotClient` не зарегистрирован → ничего не делает
- [x] **1.2** `ProcessReminders_NoUsersWithReminders_NoDelivery` — нет пользователей → нет отправок
- [x] **1.3** `ProcessUserReminder_InvalidTimezone_Skips` — невалидная timezone → skip, без ошибки
- [x] **1.4** `ProcessUserReminder_ReminderTimeNotYet_Skips` — reminder time ещё не наступило → skip
- [x] **1.5** `ProcessUserReminder_ReminderTimePassed10Min_Skips` — reminder >10 мин назад → skip (no retroactive)
- [x] **1.6** `ProcessUserReminder_AlreadySentToday_Skips` — уже отправляли сегодня → skip
- [x] **1.7** `ProcessUserReminder_Success_CreatesDeliveryAndSends` — happy path: создаёт attempt + отправляет + статус "sent"
- [x] **1.8** `ProcessUserReminder_TelegramTransientError_StatusFailed` — 429/5xx → статус "failed" (retryable)
- [x] **1.9** `ProcessUserReminder_TelegramTerminalError_StatusTerminalFailed` — 4xx → статус "terminal_failed"
- [x] **1.10** `ProcessUserReminder_DstSpringForward_AdjustsTime` — DST spring-forward → корректировка через dstDelta
- [x] **1.11** `ProcessUserReminder_DstFallBack_UsesFirstOccurrence` — DST fall-back → использует первое вхождение
- [x] **1.12** `ProcessUserReminder_WithinWindow_SendsReminder` — reminder в пределах 0-10 мин окна → отправляет

---

## Фаза 2: DeliveryRetryService (unit-тесты) ✅

**Файл**: `tests/DayTrace.Tests/Services/DeliveryRetryServiceTests.cs`

**Изменения в production-коде**:
- `DeliveryRetryService`: `private` → `internal` для `ProcessRetriesAsync`

### Тесты (15 тестов):

- [x] **2.1** `ProcessRetries_NoBotClient_Returns` — нет бот-клиента → выход
- [x] **2.2** `ProcessRetries_NoRetryable_Returns` — нет retryable записей → выход
- [x] **2.3** `ProcessRetries_BackoffNotReady_Skips` — backoff ещё не истёк → skip
- [x] **2.4** `ProcessRetries_BackoffReady_Processes` — backoff истёк → обрабатывает
- [x] **2.5** `ProcessRetries_UserNotFound_TerminalFailed` — пользователь не найден → terminal_failed
- [x] **2.6** `ProcessRetries_UserNoTelegramId_TerminalFailed` — TelegramUserId ≤ 0 → terminal_failed
- [x] **2.7** `ProcessRetries_ReminderType_CorrectText` — тип "reminder" → правильный текст сообщения
- [x] **2.8** `ProcessRetries_SoftReminderType_ResolvePeriodName` — тип "soft_reminder" → текст с именем периода
- [x] **2.9** `ProcessRetries_AdminBroadcast_UseCampaignText` — тип "admin_broadcast" → текст из кампании
- [x] **2.10** `ProcessRetries_AdminBroadcast_NoCampaign_TerminalFailed` — broadcast без кампании → terminal_failed
- [x] **2.11** `ProcessRetries_AdminBroadcast_NoReferenceId_TerminalFailed` — broadcast без reference_id → terminal_failed
- [x] **2.12** `ProcessRetries_SendSuccess_StatusSent` — успешная отправка → статус "sent"
- [x] **2.13** `ProcessRetries_TransientError_StatusFailed` — transient ошибка → "failed" (для retry)
- [x] **2.14** `ProcessRetries_TransientError_MaxAttempts_TerminalFailed` — transient на 5-й попытке → terminal_failed
- [x] **2.15** `ProcessRetries_PendingAdminBroadcast_SkipsBackoff` — pending admin_broadcast → пропускает проверку backoff

---

## Фаза 3: BotUpdateHandler (unit-тесты) ✅

**Файл**: `tests/DayTrace.Tests/Services/BotUpdateHandlerTests.cs`

### Тесты (13 тестов):

- [x] **3.1** `HandleUpdate_StartCommand_NewUser_SendsWelcome` — /start для нового пользователя → welcome с "Добро пожаловать"
- [x] **3.2** `HandleUpdate_StartCommand_ExistingUser_SendsWelcomeBack` — /start для существующего → "С возвращением"
- [x] **3.3** `HandleUpdate_StartCommand_UtcTimezone_ShowsTimezoneHint` — UTC timezone → подсказка о timezone
- [x] **3.4** `HandleUpdate_HelpCommand_SendsHelpText` — /help → текст справки
- [x] **3.5** `HandleUpdate_UnrecognizedText_SavesFeedback` — произвольный текст → сохраняет feedback
- [x] **3.6** `HandleUpdate_UnrecognizedText_LongText_Truncates` — текст >2000 → обрезает до 2000
- [x] **3.7** `HandleUpdate_UnrecognizedText_Error_SendsErrorMessage` — ошибка при сохранении → сообщение об ошибке
- [x] **3.8** `HandleUpdate_EmptyText_Ignores` — пустой текст → игнорирует
- [x] **3.9** `HandleUpdate_NullFrom_Ignores` — message.From == null → игнорирует
- [x] **3.10** `HandleUpdate_CallbackQuery_SummaryPrefix_SendsRedirect` — callback "summary_*" → текст "Выберите в приложении"
- [x] **3.11** `HandleUpdate_CallbackQuery_DedupWithin3s_Ignores` — повторный callback < 3s → игнорирует
- [x] **3.12** `HandleUpdate_CallbackQuery_After3s_Processes` — callback через > 3s → обрабатывает
- [x] **3.13** `HandleUpdate_UnknownUpdateType_NoException` — неизвестный тип update → без ошибки

---

## Итого

| Фаза | Файл | Тесты | Статус |
|-------|------|-------|--------|
| 1 | DailyReminderServiceTests.cs | 12 | ✅ |
| 2 | DeliveryRetryServiceTests.cs | 15 | ✅ |
| 3 | BotUpdateHandlerTests.cs | 13 | ✅ |
| **Итого** | **3 файла** | **40** | ✅ |

## Изменения в production-коде

1. `src/DayTrace.Api/DayTrace.Api.csproj` — `<InternalsVisibleTo Include="DayTrace.Tests" />`
2. `src/DayTrace.Api/BackgroundServices/DailyReminderService.cs` — `private` → `internal` для testable methods
3. `src/DayTrace.Api/BackgroundServices/DeliveryRetryService.cs` — `private` → `internal` для testable methods
