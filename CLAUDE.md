# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Обзор

DayTrace — сервис личных заметок и рефлексии с иерархией итогов: **день → неделя → месяц → год**. Состоит из ASP.NET Core API, Telegram Bot, Telegram Mini App (Vue 3) и Web Admin UI (Vue 3).

## Команды разработки

### Backend (.NET 9)

```bash
# Запуск PostgreSQL
docker compose up -d postgres

# Миграции EF Core
dotnet ef database update --project src/DayTrace.Infrastructure --startup-project src/DayTrace.Api

# Добавить миграцию
dotnet ef migrations add <Name> --project src/DayTrace.Infrastructure --startup-project src/DayTrace.Api

# Запуск API (порт 5000)
dotnet run --project src/DayTrace.Api
dotnet watch run --project src/DayTrace.Api   # hot reload

# Seed администратора
dotnet run --project src/DayTrace.Api -- seed-admin --email admin@example.com --password YourSecurePassword
```

### Frontend (Vue 3 + Vite + TypeScript)

```bash
# Mini App (:5173)
npm --prefix src/miniapp install && npm --prefix src/miniapp run dev

# Admin UI (:5174)
npm --prefix src/admin-ui install && npm --prefix src/admin-ui run dev

# Production builds
npm --prefix src/miniapp run build
npm --prefix src/admin-ui run build
```

### Тесты

```bash
# Все тесты (требуется Docker для Testcontainers)
dotnet test

# Один тестовый класс
dotnet test --filter "FullyQualifiedName~EventLifecycleTests"

# Один тест
dotnet test --filter "FullyQualifiedName~EventLifecycleTests.CreateEvent_ReturnsCreated"
```

Тесты используют **xUnit** (не NUnit) + **Testcontainers.PostgreSql** (реальный PostgreSQL 16 в контейнере) + **Moq**. Docker daemon должен быть запущен.

### Docker Compose (полный стек)

```bash
docker compose up -d --build
docker compose logs -f api
docker compose down
```

Сервисы: `postgres` (:5432), `api` (:5005 → container :8080), `miniapp` (:5173), `admin-ui` (:5174).

**Важно**: при локальной разработке без Docker API слушает `:5000`, а в Docker Compose маппинг `5005:8080`.

## Архитектура

### Clean Architecture

```
DayTrace.Domain          → Сущности, интерфейсы репозиториев, доменные сервисы (нет зависимостей от EF/ASP.NET)
DayTrace.Infrastructure  → EF Core DbContext, репозитории, миграции, NLog-логгер
DayTrace.Api             → Контроллеры, middleware, background services, DI-конфигурация
DayTrace.Bot             → Telegram Bot handlers, DI-регистрация
```

`Directory.Build.props` — общие настройки: `net9.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.

**Зависимости**: Domain ← Infrastructure ← Bot ← Api. Bot ссылается напрямую на Infrastructure (не только Domain) для доступа к репозиториям через DI.

### Middleware pipeline (порядок важен)

```
CorrelationId → GlobalExceptionHandler → CORS → SessionAuth → AdminAuth → ClientOperationId → Controllers
```

- `SessionAuthMiddleware` — анонимные пути в HashSet: `/auth/telegram`, `/health`, `/bot/webhook`, `/swagger`, `/admin/`. Sliding renewal 24h. Устанавливает `HttpContext.Items["UserId"]`, `["User"]`, `["Timezone"]` (доступ через extension methods).
- `AdminAuthMiddleware` — RBAC через маршруты: analyst (metrics/dashboard), operator (users/content), admin (audit). Роли иерархичны: `admin > operator > analyst`.
- `ClientOperationIdMiddleware` — обязателен для POST/PATCH/DELETE. Dedup через `operation_id_cache` (5 мин TTL). При не-2xx удаляет pending claim для retry.

### Background services (IHostedService)

- `PeriodJobWorkerService` (5s) — обработка period_jobs (SELECT FOR UPDATE SKIP LOCKED, max 5 jobs/cycle, lease_id fencing)
- `StuckJobReaperService` — таймаут зависших jobs + retry
- `BotPollingService` — Telegram Bot long-polling (активен только если `TelegramBot__WebhookBaseUrl` пустой)
- `DailyReminderService` (60s) — напоминания с DST handling (spring-forward/fall-back)
- `DeliveryRetryService` — повторная доставка (exponential backoff)
- `OperationIdCleanupService`, `UserPurgeService`, `AuditLogCleanupService` — фоновая очистка

### Аутентификация

- **User:** opaque Bearer token → SHA-256 hash в `user_sessions` (sliding 24h TTL). Middleware → `HttpContext.Items["UserId"]`, `["Timezone"]`.
- **Admin:** отдельный token → `admin_sessions` (8h, без renewal). RBAC: `admin > operator > analyst`.
- **Bot webhook:** `X-Telegram-Bot-Api-Secret-Token`.
- **Replay protection:** SHA-256 от init_data, TTL 300s в `auth_replay_cache`.

### Concurrency model (PeriodJob)

Транзакционная идемпотентность:
- `idempotency_key = "{userId}_{periodType}_{start}_{end}_{runNumber}"` в `period_jobs`
- `SELECT FOR UPDATE SKIP LOCKED` для claim jobs
- Fencing через `lease_id` + `target_summary_version` при записи результата (`FencedUpdateAsync`)
- Job statuses: `pending → running → success/failed/superseded`
- Два режима: `AutoTrigger` (skip если 0 событий или summary generated) и `ForceRerun` (инкрементирует RunNumber)

### API conventions

- JSON snake_case (`SnakeCaseLower` JsonNamingPolicy + `PropertyNameCaseInsensitive = true`)
- Даты: UTC в БД, `DateOnly` для `local_date`, `DateTime` (UTC) для timestamps
- Soft-delete через `deleted_at`
- Логирование: NLog с `correlation_id` (через `IDomainLogger` — Singleton)
- Client dedup: `X-Client-Operation-Id` header (uuid v4, 5 мин TTL)
- Комментарии в коде ссылаются на требования: `US-XXX`, `FR-XX` (см. `PRD.md`)
- **Pagination**: User API — cursor-based (base64-encoded `"{localDate}|{createdAt}|{id}"`), Admin API — offset-based (limit/offset)
- Event edit window: 168 часов (7 дней), backdate до 30 дней

### Bot

- `BotUpdateHandler` — команды `/start`, `/help`, `/settings`; текст → pending event → inline keyboard (importance ★-★★★★★)
- **In-memory state**: `static ConcurrentDictionary<long, (string, DateTime)> PendingEvents` (TTL 5 мин) и `RecentCallbacks` (3s dedupe). Теряется при рестарте, не работает при горизонтальном масштабировании.
- URL Mini App в кнопке: `https://daytrace.app`
- Режим webhook vs polling: определяется наличием `TelegramBot__WebhookBaseUrl`

### Frontend архитектура

**miniapp** (Telegram Mini App):
- Views: Today, Week, Month, Year, Settings (bottom tabs)
- Pinia stores: `auth`, `settings`
- `useTelegram` composable — прямой доступ к `window.Telegram.WebApp` (не через `@telegram-apps/sdk` API)
- Axios interceptor: auto Bearer header + `X-Client-Operation-Id` (uuid) для мутаций + 401 → clearAuth
- Темизация через Telegram CSS-переменные (`--tg-bg-color`, etc.)
- Без Vite proxy — требует CORS для dev

**admin-ui** (Web Admin):
- Views: Login, Dashboard, Users, UserDetail, Content, Operations, Audit
- Route guards с RBAC: `{ minRole: 'analyst' | 'operator' | 'admin' }`
- Auth state в `localStorage` (`admin_token`, `admin_role`, `admin_email`)
- Vite proxy: `/api` → `http://localhost:5000` (rewrite убирает `/api` prefix)

### Тестовая инфраструктура

- `PostgresFixture` — shared Testcontainers fixture (collection `"Postgres"`)
- `DayTraceWebFactory` — `WebApplicationFactory<Program>` с подменой DbContext, среда `"Testing"`
- Хелперы: `CreateAuthenticatedClientAsync()`, `CreateAdminUserAsync(role)`, `CreateAdminUserWithCredentialsAsync(role)`, `CleanDatabaseAsync()` (DELETE в FK-safe порядке)
- `tests/DayTrace.Tests/Integration/` — интеграционные тесты (auth, events, summaries, admin RBAC)
- `tests/DayTrace.Tests/Services/` — unit-тесты доменных сервисов (Moq)
- `public partial class Program { }` в Program.cs — обязательно для `WebApplicationFactory`

### Известные особенности

- **Timezone**: IANA строки, `TimeZoneInfo.FindSystemTimeZoneById()` на .NET 9 работает с IANA на Linux. На Windows без `TimeZoneConverter` пакета может быть проблема — production в Docker (Linux).
- **Admin email uniqueness**: case-insensitive unique index создаётся через raw SQL в миграции (не через EF Fluent API).
- **DbContext маппинг**: все таблицы/колонки в snake_case, Summary.Content и AuditLog.Payload — `jsonb`.
- **DI lifetimes**: `IDomainLogger` — Singleton; `ITelegramBotClient` — Singleton; DbContext, репозитории, domain services — Scoped.

## Документация

- `PRD.md` — продуктовые требования v2.16 (функциональные/нефункциональные требования, data model, concurrency)
- `METRICS.md` — спецификация метрик (DAU/WAU/MAU, conversion, формулы)
- `docs/README.md` — индекс документации
