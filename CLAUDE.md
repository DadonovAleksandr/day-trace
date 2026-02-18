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

# Запуск API
dotnet run --project src/DayTrace.Api
dotnet watch run --project src/DayTrace.Api   # hot reload

# Seed администратора
dotnet run --project src/DayTrace.Api -- seed-admin --email admin@example.com --password YourSecurePassword
```

### Frontend (Vue 3 + Vite + TypeScript)

```bash
# Mini App (:5173)
cd src/miniapp && npm install && npm run dev

# Admin UI (:5174)
cd src/admin-ui && npm install && npm run dev

# Production build (оба)
npm run build   # vue-tsc -b && vite build
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

Тесты используют **xUnit** + **Testcontainers.PostgreSql** (реальный PostgreSQL в контейнере) + **Moq**. Docker daemon должен быть запущен.

### Docker Compose (полный стек)

```bash
docker compose up -d --build
docker compose logs -f api
docker compose down
```

Сервисы: `postgres` (:5432), `api` (:5000), `miniapp` (:5173), `admin-ui` (:5174).

## Архитектура

### Clean Architecture

```
DayTrace.Domain          → Сущности, интерфейсы репозиториев, доменные сервисы (нет зависимостей от EF/ASP.NET)
DayTrace.Infrastructure  → EF Core DbContext, репозитории, миграции, NLog-логгер
DayTrace.Api             → Контроллеры, middleware, background services, DI-конфигурация
DayTrace.Bot             → Telegram Bot handlers, DI-регистрация
```

`Directory.Build.props` — общие настройки для всех .NET-проектов: `net9.0`, `Nullable=enable`, `LangVersion=latest`.

### Middleware pipeline (порядок важен)

```
CorrelationId → GlobalExceptionHandler → CORS → SessionAuth → AdminAuth → ClientOperationId → Controllers
```

### Background services (IHostedService)

- `PeriodJobWorkerService` — обработка period_jobs (SELECT FOR UPDATE SKIP LOCKED, lease_id fencing)
- `StuckJobReaperService` — таймаут зависших jobs + retry
- `BotPollingService` — Telegram Bot long-polling
- `DailyReminderService` — ежедневные напоминания
- `DeliveryRetryService` — повторная доставка (exponential backoff)
- `OperationIdCleanupService`, `UserPurgeService`, `AuditLogCleanupService` — фоновая очистка

### Аутентификация

- **User:** opaque Bearer token → SHA-256 hash в `user_sessions` (sliding 24h TTL). Middleware → `HttpContext.Items["UserId"]`, `["Timezone"]`.
- **Admin:** отдельный token → `admin_sessions` (8h, без renewal). RBAC: `admin > operator > analyst`.
- **Bot webhook:** `X-Telegram-Bot-Api-Secret-Token`.
- **Replay protection:** SHA-256 от init_data, TTL 300s в `auth_replay_cache`.

### Concurrency model

Транзакционная идемпотентность: `idempotency_key` в `period_jobs`, `SELECT FOR UPDATE SKIP LOCKED`, fencing через `lease_id` и `target_summary_version`.

### API conventions

- JSON snake_case (`SnakeCaseLower` JsonNamingPolicy)
- Даты: UTC в БД, `DateOnly` для `local_date`, `DateTime` (UTC) для timestamps
- Soft-delete через `deleted_at`
- Логирование: NLog с `correlation_id`
- Client dedup: `client_operation_id` header (5 мин TTL в `operation_id_cache`)
- Комментарии в коде ссылаются на требования: `US-XXX`, `FR-XX` (см. `PRD.md`)

### Frontend архитектура

**miniapp** (Telegram Mini App):
- Views: Today, Week, Month, Year, Settings (bottom tabs)
- Pinia stores: `auth`, `settings`
- Composable `useTelegram` — init data, timezone, theme params
- Темизация через Telegram CSS-переменные (`--tg-bg-color`, etc.)
- `@telegram-apps/sdk` v3 для интеграции

**admin-ui** (Web Admin):
- Views: Login, Dashboard, Users, UserDetail, Content, Operations, Audit
- Route guards с RBAC: `{ minRole: 'analyst' | 'operator' | 'admin' }`
- Vite proxy: `/api` → `http://localhost:5000`

### Тестовая инфраструктура

- `PostgresFixture` — shared Testcontainers fixture для всех интеграционных тестов
- `DayTraceWebFactory` — `WebApplicationFactory<Program>` с подменой БД, хелперы `CreateAuthenticatedClientAsync`, `CreateAdminUserAsync`, `CleanDatabaseAsync`
- `tests/DayTrace.Tests/Integration/` — интеграционные тесты (auth, events, summaries, admin RBAC)
- `tests/DayTrace.Tests/Services/` — unit-тесты доменных сервисов

## Документация

- `PRD.md` — продуктовые требования v2.16 (функциональные/нефункциональные требования, data model, concurrency)
- `METRICS.md` — спецификация метрик (DAU/WAU/MAU, conversion, формулы)
