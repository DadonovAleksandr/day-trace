# Implementation Status (2026-02-24)

Актуальный статус реализации, проверенный по нескольким последним коммитам.
Этот документ нужен как мост между `README.md` (текущая эксплуатация) и `docs/PRD.md` (актуальные продуктовые требования и API-контракт).

## Проверенные коммиты

- `c425d70` — feat: broadcast campaign queue system with delivery tracking
- `d8ed620` — docs: update CLAUDE.md with messaging, info page, and auth cookie details (частично уже учтено в инженерной документации)
- `1d612d7` — feat: admin audit logging, messaging, and month/year highlight selection
- `8817634` — fix: resolve EF Core Relational version conflict with Npgsql provider (добавлена dev-заметка в `README.md`)
- `dbc620d` — feat: replace PeriodJob auto-generation with manual highlight event selection
- `6a3aa60` — style: локализация дашборда и динамическая иконка календаря (UI/polish; отдельного обновления проектных docs не требует)
- `a44340e` — feat(miniapp): improve Telegram platform integration and UX
- `dcc4bae` — fix: add Telegram safe area insets and update docs

## Ключевые изменения в текущей реализации

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

## Как читать PRD сейчас

- `docs/PRD.md` — актуальный current-state PRD без архивных разделов.
- Этот документ (`docs/IMPLEMENTATION_STATUS.md`) используйте как changelog по проверенным коммитам и быстрый обзор недавних изменений реализации.
- Для фактического поведения ориентируйтесь на:
  - `README.md`
  - `docs/PRD.md`
  - `docs/RUNTIME_WORKERS.md`
  - этот документ (`docs/IMPLEMENTATION_STATUS.md`)
