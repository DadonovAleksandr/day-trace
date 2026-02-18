# Runtime Workers

Фоновые сервисы (`HostedService`) запускаются внутри процесса API и регистрируются в `src/DayTrace.Api/Program.cs` через `AddHostedService<...>()`.

## Список сервисов

| Сервис | Назначение | Частота / триггер |
|---|---|---|
| `BotPollingService` | Long polling Telegram-обновлений и передача в `BotUpdateHandler`. | Активен только если `TelegramBot__WebhookBaseUrl` (или `TELEGRAM_WEBHOOK_BASE_URL`) пустой. Цикл `GetUpdates(timeout: 30s)`, при ошибке пауза `5s`. |
| `OperationIdCleanupService` | Очистка истёкшего кэша client operation id (идемпотентность API). | Каждую `1` минуту. Удаляет записи старше `5` минут, батч до `1000`. |
| `PeriodJobWorkerService` | Обработка `period_jobs`: claim pending/retried, генерация summary, fenced finalize. | Каждые `5s`, до `5` задач за цикл. |
| `StuckJobReaperService` | Три фазы: reaper зависших job, retry failed job, reconciliation terminal-failed job. | Каждые `2` минуты. Reap: `running > 5 мин` (до `20`). Retry: `failed`, `attempt_count < 3`, backoff elapsed (до `10`). Reconcile: `failed`, `attempt_count >= 3`, `finished_at > 5 мин`, `reconciled_at IS NULL` (до `10`). |
| `DailyReminderService` | Отправка ежедневных reminder-сообщений по локальному времени пользователя с учётом DST. | Каждые `60s`. Отправка при достижении `scheduledUtc` (окно: не позже чем `+10` минут), без дублей за день. |
| `DeliveryRetryService` | Повторная отправка неуспешных Telegram delivery attempts. | Каждые `30s`. Берёт `status=failed`, `attempt_number < 5` (до `20` за цикл), backoff `30s * 2^(attempt-1)`. |
| `UserPurgeService` | Hard-delete PII для пользователей, давно soft-deleted. | Старт после задержки `5` минут, затем каждые `24h`. Кандидаты: `status=deleted` и `deleted_at < now-30d`, батч `10`. |
| `AuditLogCleanupService` | Удаление старых audit log записей. | Старт после задержки `10` минут, затем каждые `24h`. Чистит записи старше `180` дней, батчами по `1000` до исчерпания. |

## Конфигурация и переменные окружения

Интервалы и лимиты самих воркеров в текущей реализации захардкожены в коде (`TimeSpan`/`const`) и не вынесены в env.

Переменные, которые влияют на runtime-поведение воркеров:

| Внешняя env (docker/.env) | .NET key | Влияние |
|---|---|---|
| `TELEGRAM_WEBHOOK_BASE_URL` | `TelegramBot__WebhookBaseUrl` | Переключает режим бота: если пусто — работает `BotPollingService`; если задано — polling отключается (режим webhook). |
| `TELEGRAM_BOT_TOKEN` | `TelegramBot__BotToken` | Токен Telegram Bot API. Нужен для клиента Telegram: polling, `DailyReminderService`, `DeliveryRetryService`. |
| `TELEGRAM_WEBHOOK_SECRET` | `TelegramBot__WebhookSecretToken` | Используется при webhook-входе (валидация `X-Telegram-Bot-Api-Secret-Token`), косвенно влияет на доставку апдейтов в систему в webhook-режиме. |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | Доступ к PostgreSQL. На БД завязаны все воркеры, кроме чистого чтения Telegram-апдейтов (но даже он использует БД через обработчик апдейтов). |

## Связанные гарантии надежности

- `ClientOperationIdMiddleware`: дедупликация по `X-Client-Operation-Id` кэширует только `2xx`-ответы; для неуспешных ответов pending-claim удаляется, чтобы повторная попытка была возможна.
- `PeriodJobWorkerService`: при ошибках статус summary переводится в `failed` только при совпадении версии (`summary.version == target_summary_version`), чтобы устаревший воркер не перетёр более новую генерацию.
- `UserRegistrationService`: регистрация устойчива к гонке параллельных запросов по `telegram_user_id` (fallback на пере-чтение после уникального конфликта).
