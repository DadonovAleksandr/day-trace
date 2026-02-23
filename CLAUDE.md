# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Обзор

DayTrace — сервис личных заметок и рефлексии с иерархией итогов: **день → неделя → месяц → год**. Состоит из ASP.NET Core API, Telegram Bot, Telegram Mini App (Vue 3) и Web Admin UI (Vue 3).

## Команды разработки

### Backend (.NET 10)

```bash
# Запуск PostgreSQL (порт 5433 на хосте → 5432 в контейнере)
docker compose up -d postgres

# Миграции EF Core (DesignTimeDbContextFactory — не требует --startup-project)
dotnet ef database update --project src/DayTrace.Infrastructure
dotnet ef migrations add <Name> --project src/DayTrace.Infrastructure

# Запуск API (порт 5000 при локальной разработке)
dotnet run --project src/DayTrace.Api
dotnet watch run --project src/DayTrace.Api   # hot reload

# Seed администратора (вариант 1: env vars при старте)
ADMIN_SEED_EMAIL=admin@example.com ADMIN_SEED_PASSWORD=YourSecurePassword dotnet run --project src/DayTrace.Api

# Seed администратора (вариант 2: CLI args)
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

| Сервис | Хост:Контейнер | Описание |
|--------|----------------|----------|
| `postgres` | 5433:5432 | PostgreSQL 16 |
| `api` | 5005:8080 | .NET API + Bot + Mini App (static) |
| `admin-ui` | 5174:80 | Admin dashboard (nginx) |

**Порты**: при локальной разработке без Docker API слушает `:5000`, PostgreSQL — `:5433`. В Docker Compose API маппится `5005:8080`.

## Архитектура

### Clean Architecture

```
DayTrace.Domain          → Сущности, интерфейсы репозиториев, доменные сервисы (нет зависимостей от EF/ASP.NET)
DayTrace.Infrastructure  → EF Core DbContext, репозитории, миграции, NLog-логгер
DayTrace.Api             → Контроллеры, middleware, background services, DI-конфигурация
DayTrace.Bot             → Telegram Bot handlers, DI-регистрация
```

`Directory.Build.props` — общие настройки: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`.

**Зависимости**: Domain ← Infrastructure ← Bot ← Api. Bot ссылается напрямую на Infrastructure (не только Domain) для доступа к репозиториям через DI.

### Middleware pipeline (порядок важен)

```
CorrelationId → GlobalExceptionHandler → CORS → SessionAuth → AdminAuth → ClientOperationId → Controllers
```

- `SessionAuthMiddleware` — анонимные пути включают `/auth/telegram`, `/health`, `/bot/webhook`, `/swagger`, `/admin/`, `/wisdoms/`, `/privacy`, а также miniapp static files и SPA routes (`/today`, `/week`, `/month`, `/year`). Sliding renewal 24h. Устанавливает `HttpContext.Items["UserId"]`, `["User"]`, `["Timezone"]` (доступ через extension methods).
- `AdminAuthMiddleware` — RBAC через маршруты: analyst (metrics/dashboard), operator (users/content), admin (audit). Роли иерархичны: `admin > operator > analyst`.
- `ClientOperationIdMiddleware` — обязателен для POST/PATCH/DELETE. Dedup через `operation_id_cache` (5 мин TTL). При не-2xx удаляет pending claim для retry.

### Background services (IHostedService)

- `BotWebhookSetupService` — регистрация Telegram webhook при старте (требует `TelegramBot__WebhookBaseUrl`)
- `DailyReminderService` (60s) — напоминания с DST handling (spring-forward/fall-back)
- `DeliveryRetryService` — повторная доставка (exponential backoff)
- `OperationIdCleanupService`, `UserPurgeService`, `AuditLogCleanupService` — фоновая очистка

Подробное описание: `docs/RUNTIME_WORKERS.md`.

### Аутентификация

- **User:** opaque Bearer token → SHA-256 hash в `user_sessions` (sliding 24h TTL). Middleware → `HttpContext.Items["UserId"]`, `["Timezone"]`.
- **Admin:** отдельный token → `admin_sessions` (8h, без renewal). RBAC: `admin > operator > analyst`.
- **Bot webhook:** `X-Telegram-Bot-Api-Secret-Token`.
- **Replay protection:** SHA-256 от init_data, TTL 300s в `auth_replay_cache`.

### Highlight (итоги периодов)

Итоги периодов формируются через ручной выбор «главного события» (highlight):
- `PUT /summaries/{periodType}/highlight` — установить highlight event для периода (weekly/monthly/yearly)
- Body: `{ event_id, period_start, period_end }` → создаёт или обновляет Summary с `highlight_event_id`
- Иерархическая блокировка: weekly заблокирован при наличии monthly summary, monthly — при наличии yearly (через `EventLockService`)
- `HighlightService` — доменный сервис: валидация, проверка принадлежности события, проверка блокировки, создание/обновление summary
- `summaries.highlight_event_id` — FK → `events.id`, ON DELETE SET NULL

### API conventions

- JSON snake_case (`SnakeCaseLower` JsonNamingPolicy + `PropertyNameCaseInsensitive = true`)
- Даты: UTC в БД, `DateOnly` для `local_date`, `DateTime` (UTC) для timestamps
- Soft-delete через `deleted_at`
- Логирование: NLog с `correlation_id` (через `IDomainLogger` — Singleton)
- Client dedup: `X-Client-Operation-Id` header (uuid v4, 5 мин TTL)
- Комментарии в коде ссылаются на требования: `US-XXX`, `FR-XX` (см. `docs/PRD.md`)
- **Pagination**: User API — cursor-based (base64-encoded `"{localDate}|{createdAt}|{id}"`), Admin API — offset-based (limit/offset)
- Event edit window: 168 часов (7 дней), backdate до 30 дней
- `POST /events`: одно основное событие на `local_date`; повторная попытка создания за тот же день возвращает `409 event_exists` (+ `existing_event_id`)

### Bot

- `BotUpdateHandler` — команды `/start`, `/help`; текст → pending event → inline keyboard (importance ★-★★★★★). Команда `/settings` удалена, настройки меняются в Mini App.
- **In-memory state**: `static ConcurrentDictionary<long, (string, DateTime)> PendingEvents` (TTL 5 мин) и `RecentCallbacks` (3s dedupe). Теряется при рестарте, не работает при горизонтальном масштабировании.
- Если событие за текущий день уже есть, бот обновляет его вместо создания дубликата.
- URL Mini App в кнопке: `TelegramBot__MiniAppUrl` (fallback: `TelegramBot__WebhookBaseUrl`)
- Только webhook-режим: требует `TelegramBot__WebhookBaseUrl`

### Frontend архитектура

**miniapp** (Telegram Mini App):
- Views: Today, Week, Month, Year, Settings (bottom tabs). `Today` — journal-page с одной записью на день, inline edit/delete и встроенными оценками. `Week` — выбор highlight-события из списка 7 дней (карточки с текстом и важностью, тап для выбора, «Сохранить»/«Редактировать», блокировка замком). `Month` — список событий по дням + inline edit/delete и выбор highlight-события месяца с тем же selection UI; highlight месяца блокируется при наличии yearly summary (замок). `Year` — график по месяцам + список событий по месяцам/дням и выбор highlight-события года с тем же selection UI, без блокировки (верхний уровень).
- Pinia stores: `auth`, `settings`
- `useTelegram` composable — прямой доступ к `window.Telegram.WebApp` (не через `@telegram-apps/sdk` API)
- Axios interceptor: auto Bearer header + `X-Client-Operation-Id` (uuid) для мутаций + 401 → clearAuth
- Темизация через Telegram CSS-переменные (`--tg-bg-color`, etc.)
- Без Vite proxy — требует CORS для dev
- API раздаёт miniapp SPA из `../miniapp/dist` (StaticFiles + MapFallbackToFile); `/assets/*` кэшируются долго (`immutable`), `index.html` и корневые файлы — `no-cache`
- Публичная HTML-страница политики конфиденциальности: `GET /privacy` (без auth)

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

- **DesignTimeDbContextFactory**: `src/DayTrace.Infrastructure/Data/DesignTimeDbContextFactory.cs` — читает env var `ConnectionStrings__DefaultConnection`, fallback к `Host=localhost;Port=5433;...`. Позволяет выполнять `dotnet ef` без `--startup-project`.
- **Timezone**: IANA строки, `TimeZoneInfo.FindSystemTimeZoneById()` на .NET 10 работает с IANA на Linux. На Windows без `TimeZoneConverter` пакета может быть проблема — production в Docker (Linux).
- **Admin email uniqueness**: case-insensitive unique index создаётся через raw SQL в миграции (не через EF Fluent API).
- **DbContext маппинг**: все таблицы/колонки в snake_case, Summary.Content и AuditLog.Payload — `jsonb`.
- **DI lifetimes**: `IDomainLogger` — Singleton; `ITelegramBotClient` — Singleton; DbContext, репозитории, domain services — Scoped.

## Документация

- `docs/PRD.md` — продуктовые требования v2.16 (функциональные/нефункциональные требования, data model, concurrency)
- `docs/METRICS.md` — спецификация метрик (DAU/WAU/MAU, conversion, формулы)
- `docs/README.md` — индекс документации
- `docs/RUNTIME_WORKERS.md` — описание фоновых сервисов (интервалы, поведение, конфигурация)
