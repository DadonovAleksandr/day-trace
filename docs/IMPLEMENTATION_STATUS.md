# Implementation Status (2026-02-26)

Актуальный статус реализации, проверенный по нескольким последним коммитам.
Этот документ нужен как мост между `README.md` (текущая эксплуатация) и `docs/PRD.md` (актуальные продуктовые требования и API-контракт).

## Проверенные коммиты

### Последние (после `b53251f` UpdateDocs)

- `8dfa117` — fix: deploy health check retry instead of single attempt (deploy-скрипт: retry-цикл вместо одиночной проверки)
- `4fd3af7` — feat: CI/CD pipeline and app versioning via git tags (GitHub Actions workflow, deploy script, APP_VERSION в Dockerfiles)
- `f656db4` — test: add unit tests for background services and bot handler (40 тестов: DailyReminderService, DeliveryRetryService, BotUpdateHandler)
- `6adbc36` — test: удалён устаревший тест cooldown смены таймзоны
- `a45a932` — fix: DST handling, retry backoff, AuthController null check, HighlightService version, memory leak (resolves #2)
- `df2b4d8` — fix: security hardening — gitignore, cookie Secure flag, dev endpoint protection (resolves #1)
- `39defcb` — refactor: remove dead code, extract shared ComputeSha256, fix misleading comments (resolves #3)
- `577a310` — chore(miniapp): remove unused EmptyState import from YearView and add CI/CD plan

### Ранее проверенные

- `750937a` — fix(miniapp): add spacing in InfoView between title and section cards (UI polish)
- `58b32e3` — refactor(miniapp): redesign YearView as 12 month cards with highlight selection
- `1011d01` — fix(settings): default timezone to Europe/Moscow and remove 24h cooldown
- `bbf2e8f` — refactor(miniapp): make WeekView action buttons full-width (UI polish)
- `b7f7b6b` — refactor(miniapp): tweak TodayView layout (UI polish)
- `c425d70` — feat: broadcast campaign queue system with delivery tracking
- `d8ed620` — docs: update CLAUDE.md with messaging, info page, and auth cookie details (частично уже учтено в инженерной документации)
- `1d612d7` — feat: admin audit logging, messaging, and month/year highlight selection
- `8817634` — fix: resolve EF Core Relational version conflict with Npgsql provider (добавлена dev-заметка в `README.md`)
- `dbc620d` — feat: replace PeriodJob auto-generation with manual highlight event selection
- `6a3aa60` — style: локализация дашборда и динамическая иконка календаря (UI/polish; отдельного обновления проектных docs не требует)
- `a44340e` — feat(miniapp): improve Telegram platform integration and UX
- `dcc4bae` — fix: add Telegram safe area insets and update docs

## Ключевые изменения в текущей реализации

### 0. CI/CD pipeline и версионирование (новое)

- GitHub Actions workflow (`.github/workflows/deploy.yml`): деплой при пуше git-тега `v*.*.*` или через ручной `workflow_dispatch`.
- Deploy-скрипт (`scripts/deploy.sh`): checkout тега, сборка Docker-контейнеров с `APP_VERSION`, health check с retry-циклом (до 60 секунд, 5-секундные интервалы).
- Версия из тега встраивается в .NET assembly (`GET /health` → `version`) и Vue-фронтенды (`VITE_APP_VERSION`).
- `Dockerfile.api` и `Dockerfile.frontend` принимают `APP_VERSION` build arg (fallback `dev`).
- `docker-compose.yml` передаёт `APP_VERSION` через build args.
- Подробная инструкция: `docs/deploy-ci-cd.md`.

### 0a. Security hardening

- `.env.example` добавлен как шаблон переменных окружения (сам `.env` в `.gitignore`).
- Admin session cookie: автоматический флаг `Secure` при HTTPS или наличии `X-Forwarded-Proto: https`.
- `POST /auth/dev` защищён проверкой `ASPNETCORE_ENVIRONMENT=Development` (недоступен в Production).
- Извлечена общая утилита `CryptoUtils.ComputeSha256()` из дублированного кода хеширования.

### 0b. DST handling и exponential backoff

- `DailyReminderService`: корректная обработка spring-forward (сдвиг через `DaylightDelta`) и fall-back (использование первого вхождения).
- `DeliveryRetryService`: экспоненциальный backoff по полю `last_attempt_at` (новая миграция `20260226031927`), формула `30s * 2^(attempt-1)`.
- `HighlightService`: инкремент `Version` при обновлении существующего summary.

### 0c. Тестовое покрытие

- Добавлено 40 unit-тестов: `DailyReminderServiceTests` (DST, отправка, dedup), `DeliveryRetryServiceTests` (backoff, retry, terminal fail), `BotUpdateHandlerTests` (команды, callback dedup).
- `DayTrace.Api.csproj`: добавлен `InternalsVisibleTo` для тестового проекта.
- Удалён устаревший тест cooldown смены таймзоны.

### 1. Period summaries: manual highlight flow

- Переход с auto-generation через `period_jobs` на ручной выбор главного события периода.
- Актуальный endpoint: `PUT /summaries/{periodType}/highlight`, где `periodType` = `weekly|monthly|yearly`.
- `GET /summaries/{periodType}` возвращает `highlight_event_id`.
- В Mini App:
  - `Week` — выбор из событий недели;
  - `Month` — выбор только из weekly-highlight'ов (с клиентскими guard'ами выбора);
  - `Year` — выбор только из monthly-highlight'ов (с догрузкой данных по cursor pagination и guard'ами выбора).

### 2. Runtime workers: PeriodJob pipeline выключен

- В `src/DayTrace.Api/Program.cs` зарегистрированы только:
  - `BotWebhookSetupService` (условно, при наличии bot token),
  - `OperationIdCleanupService`,
  - `DailyReminderService`,
  - `DeliveryRetryService`,
  - `UserPurgeService`,
  - `AuditLogCleanupService`.
- `DeliveryRetryService` теперь выполняет две роли: retry для `failed` delivery attempts и обработку очереди `pending` `admin_broadcast` попыток (кампании рассылки из Admin UI).
- `PeriodJobWorkerService` и `StuckJobReaperService` не зарегистрированы и не выполняются.

### 3. Admin UI/API: messaging + audit + feedback

- Добавлены endpoints:
  - `POST /admin/messaging/broadcast`
  - `GET /admin/messaging/broadcasts`
  - `GET /admin/messaging/broadcasts/{id}`
  - `GET /admin/delivery-attempts`
  - `GET /admin/audit-logs`
  - `GET /admin/feedback`
  - `PATCH /admin/feedback/{id}/read`
  - `POST /admin/feedback/{id}/reply`
- Массовая рассылка переведена на queue-based flow: `POST /admin/messaging/broadcast` создаёт campaign и `pending` `delivery_attempts` (аудитории `active`/`reminders`), а отправка выполняется асинхронно через `DeliveryRetryService`.
- `GET /admin/messaging/broadcasts*` возвращают кампании с агрегированными delivery stats (`pending/sent/failed/terminal_failed`) и производным статусом кампании (`queued/processing/completed/partial_failed/failed`).
- Admin UI (`Operations`) теперь включает массовые рассылки, список кампаний и просмотр delivery attempts.
- Audit UI поддерживает просмотр/фильтрацию журнала admin-действий.
- `Content` покрывает feedback workflow: список, mark-as-read и ответ пользователю в Telegram с audit-логированием.

### 4. Admin auth: cookie-based session helper

- Используется HttpOnly-cookie `daytrace_admin_session` (TTL 8 часов, `SameSite=Strict`, path `/`).
- При отсутствии cookie поддерживается fallback на `Authorization: Bearer`.

### 5. Mini App: Telegram-native UX и safe area

- Учитываются Telegram safe area inset'ы и высота виртуальной клавиатуры (`--dt-safe-*`, `--dt-keyboard-height`).
- Поддержаны `themeChanged`, Telegram `BackButton` (без утечек callback'ов), haptic feedback.
- Черновики в `Today` автосохраняются по дате (`dt_draft_<YYYY-MM-DD>`).
- Добавлена/расширена вкладка `Info` с пользовательскими разделами и контактами.

### 6. Timezone: дефолт Europe/Moscow, без cooldown

- Дефолтный timezone при регистрации и fallback'ах изменён с `UTC` на `Europe/Moscow`.
- 24-часовой cooldown на смену timezone убран — смена доступна сразу.

### 7. YearView: карточки месяцев

- Экран `Year` в Mini App переделан: вместо графика и grouped events теперь 12 карточек месяцев (аналогично `Week`/`Month`).
- Выбор highlight-события года выполняется из monthly-highlight'ов в едином selection UI.

## Как читать PRD сейчас

- `docs/PRD.md` — актуальный current-state PRD без архивных разделов.
- Этот документ (`docs/IMPLEMENTATION_STATUS.md`) используйте как changelog по проверенным коммитам и быстрый обзор недавних изменений реализации.
- Для фактического поведения ориентируйтесь на:
  - `README.md`
  - `docs/PRD.md`
  - `docs/RUNTIME_WORKERS.md`
  - этот документ (`docs/IMPLEMENTATION_STATUS.md`)
