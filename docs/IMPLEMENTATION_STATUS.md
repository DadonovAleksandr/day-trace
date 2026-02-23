# Implementation Status (2026-02-23)

Актуальный статус реализации, проверенный по нескольким последним коммитам.
Этот документ нужен как мост между `README.md` (текущая эксплуатация) и `docs/PRD.md` (источник требований, местами с историческим дизайном).

## Проверенные коммиты

- `d8ed620` — docs: update CLAUDE.md with messaging, info page, and auth cookie details (частично уже учтено в инженерной документации)
- `1d612d7` — feat: admin audit logging, messaging, and month/year highlight selection
- `8817634` — fix: resolve EF Core Relational version conflict with Npgsql provider (документация не требуется)
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
  - `Month` — выбор из weekly-highlight'ов;
  - `Year` — выбор из monthly-highlight'ов.

### 2. Runtime workers: PeriodJob pipeline выключен

- В `src/DayTrace.Api/Program.cs` зарегистрированы только:
  - `BotWebhookSetupService` (условно, при наличии bot token),
  - `OperationIdCleanupService`,
  - `DailyReminderService`,
  - `DeliveryRetryService`,
  - `UserPurgeService`,
  - `AuditLogCleanupService`.
- `PeriodJobWorkerService` и `StuckJobReaperService` не зарегистрированы и не выполняются.

### 3. Admin UI/API: messaging + audit

- Добавлены endpoints:
  - `POST /admin/messaging/broadcast`
  - `GET /admin/messaging/broadcasts`
  - `GET /admin/messaging/broadcasts/{id}`
  - `GET /admin/delivery-attempts`
  - `GET /admin/audit-logs`
- Admin UI (`Operations`) теперь включает массовые рассылки, список кампаний и просмотр delivery attempts.
- Audit UI поддерживает просмотр/фильтрацию журнала admin-действий.

### 4. Admin auth: cookie-based session helper

- Используется HttpOnly-cookie `daytrace_admin_session` (TTL 8 часов, `SameSite=Strict`, path `/`).
- При отсутствии cookie поддерживается fallback на `Authorization: Bearer`.

### 5. Mini App: Telegram-native UX и safe area

- Учитываются Telegram safe area inset'ы и высота виртуальной клавиатуры (`--dt-safe-*`, `--dt-keyboard-height`).
- Поддержаны `themeChanged`, Telegram `BackButton` (без утечек callback'ов), haptic feedback.
- Черновики в `Today` автосохраняются по дате (`dt_draft_<YYYY-MM-DD>`).
- Добавлена/расширена вкладка `Info` с пользовательскими разделами и контактами.

## Как читать PRD сейчас

- `docs/PRD.md` остаётся источником продуктовых требований.
- Разделы с `period_jobs`/`/summaries/{periodType}/run` описывают исторический или целевой дизайн и не совпадают с текущей реализацией.
- Для фактического поведения ориентируйтесь на:
  - `README.md`
  - `docs/RUNTIME_WORKERS.md`
  - этот документ (`docs/IMPLEMENTATION_STATUS.md`)
