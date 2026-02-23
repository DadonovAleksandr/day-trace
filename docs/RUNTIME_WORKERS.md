# Runtime Workers

Фоновые сервисы (`HostedService`) запускаются внутри процесса API и регистрируются в `src/DayTrace.Api/Program.cs` через `AddHostedService<...>()`.

## Список сервисов

| Сервис | Назначение | Частота / триггер |
|---|---|---|
| `BotWebhookSetupService` | Регистрация Telegram webhook при старте приложения. | Однократно при старте. Требует `TelegramBot__WebhookBaseUrl`. |
| `OperationIdCleanupService` | Очистка истёкшего кэша client operation id (идемпотентность API). | Каждую `1` минуту. Удаляет записи старше `5` минут, батч до `1000`. |
| `DailyReminderService` | Отправка ежедневных reminder-сообщений по локальному времени пользователя с учётом DST. | Каждые `60s`. Отправка при достижении `scheduledUtc` (окно: не позже чем `+10` минут), без дублей за день. |
| `DeliveryRetryService` | Повторная отправка неуспешных Telegram delivery attempts. | Каждые `30s`. Берёт `status=failed`, `attempt_number < 5` (до `20` за цикл), backoff `30s * 2^(attempt-1)`. |
| `UserPurgeService` | Hard-delete PII для пользователей, давно soft-deleted. | Старт после задержки `5` минут, затем каждые `24h`. Кандидаты: `status=deleted` и `deleted_at < now-30d`, батч `10`. |
| `AuditLogCleanupService` | Удаление старых audit log записей. | Старт после задержки `10` минут, затем каждые `24h`. Чистит записи старше `180` дней, батчами по `1000` до исчерпания. |

`PeriodJobWorkerService` и `StuckJobReaperService` больше не зарегистрированы в `Program.cs` и не выполняются внутри API-процесса.
Текущий пользовательский flow для периодов week/month/year — ручной выбор `highlight`-события через `PUT /summaries/{periodType}/highlight` (см. `docs/IMPLEMENTATION_STATUS.md`).

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
- `DeliveryRetryService`: повторные Telegram delivery attempts идут с экспоненциальным backoff и ограничением по числу попыток (`attempt_number < 5`), после чего запись переводится в terminal-failed состояние.
- `UserRegistrationService`: регистрация устойчива к гонке параллельных запросов по `telegram_user_id` (fallback на пере-чтение после уникального конфликта).
