# Runtime Workers

Фоновые сервисы (`HostedService`) запускаются внутри процесса API и регистрируются в `src/DayTrace.Api/Program.cs` через `AddHostedService<...>()`.

## Список сервисов

| Сервис | Назначение | Частота / триггер |
|---|---|---|
| `BotWebhookSetupService` | Регистрация Telegram webhook при старте приложения. | Однократно при старте. Требует `TelegramBot__WebhookBaseUrl`. |
| `OperationIdCleanupService` | Очистка истёкшего кэша client operation id (идемпотентность API). | Каждую `1` минуту. Удаляет записи старше `5` минут, батч до `1000`. |
| `DailyReminderService` | Отправка ежедневных reminder-сообщений по локальному времени пользователя с корректной обработкой DST (spring-forward / fall-back). | Каждые `60s`. Отправка при достижении `scheduledUtc` (окно: не позже чем `+10` минут), без дублей за день. |
| `DeliveryRetryService` | Отправка и retry Telegram delivery attempts (включая очередь admin broadcast campaigns). | Каждые `30s`. Берёт `status=failed`, `attempt_number < 5`, а также `pending` для `admin_broadcast` (до `20` за цикл): `failed` идут с backoff `30s * 2^(attempt-1)` по полю `last_attempt_at`, `pending admin_broadcast` обрабатываются без задержки как первичная отправка. |
| `UserPurgeService` | Hard-delete PII для пользователей, давно soft-deleted. | Старт после задержки `5` минут, затем каждые `24h`. Кандидаты: `status=deleted` и `deleted_at < now-30d`, батч `10`. |
| `AuditLogCleanupService` | Удаление старых audit log записей. | Старт после задержки `10` минут, затем каждые `24h`. Чистит записи старше `180` дней, батчами по `1000` до исчерпания. |

### DST-обработка в DailyReminderService

`DailyReminderService` корректно обрабатывает переходы на летнее/зимнее время:
- **Spring-forward** (провал во времени): если локальное время напоминания попадает в несуществующий интервал, сервис сдвигает его вперёд на величину DST-перехода (`TimeZoneInfo.GetAdjustmentRules()`, `DaylightDelta`).
- **Fall-back** (неоднозначное время): если локальное время попадает в интервал неоднозначности, сервис использует первое вхождение (большее UTC-смещение).

### Exponential backoff в DeliveryRetryService

Повторные попытки доставки используют поле `last_attempt_at` (добавлено в миграции `20260226031927`) для расчёта экспоненциального backoff:
- Формула задержки: `30 * 2^(attempt_number - 1)` секунд (30s → 60s → 120s → 240s).
- После 5-й попытки запись переводится в `terminal_failed`.
- Для `pending admin_broadcast` backoff не применяется (немедленная первичная отправка).
- Если `last_attempt_at` не заполнено, используется `created_at`.

`PeriodJobWorkerService` и `StuckJobReaperService` больше не зарегистрированы в `Program.cs` и не выполняются внутри API-процесса.
Текущий пользовательский flow для периодов week/month/year — ручной выбор `highlight`-события через `PUT /summaries/{periodType}/highlight` (см. `docs/IMPLEMENTATION_STATUS.md`).
Начиная с queue-based broadcast flow (`c425d70`), `POST /admin/messaging/broadcast` не отправляет Telegram-сообщения inline: controller ставит `admin_broadcast` attempts в очередь (`pending`), а доставку/ретраи выполняет `DeliveryRetryService`.

## Конфигурация и переменные окружения

Интервалы и лимиты самих воркеров в текущей реализации захардкожены в коде (`TimeSpan`/`const`) и не вынесены в env.
Для изменения поведения правьте соответствующие классы в `src/DayTrace.Api/BackgroundServices/` и затем проверьте интеграционные тесты/наблюдаемость.

Бот работает в webhook-only режиме: polling-воркера в приложении нет.
`BotWebhookSetupService` нужен только при включённом Telegram Bot (`TELEGRAM_BOT_TOKEN`) и выполняется best-effort при старте процесса.

Переменные, которые влияют на runtime-поведение воркеров:

| Внешняя env (docker/.env) | .NET key | Влияние |
|---|---|---|
| `TELEGRAM_WEBHOOK_BASE_URL` | `TelegramBot__WebhookBaseUrl` | Обязательная переменная: URL для регистрации webhook. Без неё бот не получает обновления. |
| `TELEGRAM_BOT_TOKEN` | `TelegramBot__BotToken` | Токен Telegram Bot API. Нужен для Telegram-клиента в webhook setup, `DailyReminderService`, `DeliveryRetryService` и обработчиках бота. |
| `TELEGRAM_WEBHOOK_SECRET` | `TelegramBot__WebhookSecretToken` | Используется при webhook-входе (валидация `X-Telegram-Bot-Api-Secret-Token`), косвенно влияет на доставку апдейтов в систему в webhook-режиме. |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | Доступ к PostgreSQL. На БД завязаны все воркеры и обработка webhook-апдейтов. |

## Связанные гарантии надежности

- `ClientOperationIdMiddleware`: дедупликация по `X-Client-Operation-Id` кэширует только `2xx`-ответы; для неуспешных ответов pending-claim удаляется, чтобы повторная попытка была возможна.
- `DeliveryRetryService`: `failed` попытки идут с экспоненциальным backoff (`30s * 2^(attempt-1)`, по `last_attempt_at`) и ограничением по числу попыток (`attempt_number < 5`), после чего запись переводится в terminal-failed состояние; queued `admin_broadcast` (`pending`) обрабатываются как первичная отправка без backoff.
- Кампании admin broadcast берут прогресс/агрегированные статусы из `delivery_attempts`; UI/API статусы кампаний зависят от фактического состояния этих записей, а не от синхронного ответа `POST /admin/messaging/broadcast`.
- `UserRegistrationService`: регистрация устойчива к гонке параллельных запросов по `telegram_user_id` (fallback на пере-чтение после уникального конфликта).
