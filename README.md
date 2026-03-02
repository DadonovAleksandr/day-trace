# DayTrace

DayTrace — сервис личных заметок и рефлексии с иерархией итогов:
**день → неделя → месяц → год**.

## MVP
- Telegram Bot
- Telegram Mini App
- Web Admin UI

## Документация
- `docs/README.md` — индекс документации
- `docs/IMPLEMENTATION_STATUS.md` — актуальный статус реализации и changelog по проверенным коммитам
- `docs/PRD.md` — продуктовые требования
- `docs/METRICS.md` — спецификация метрик
- `docs/RUNTIME_WORKERS.md` — фоновые сервисы, интервалы, DST-обработка
- `docs/deploy-ci-cd.md` — CI/CD через GitHub Actions, версионирование через git-теги
- `CLAUDE.md` — правила и рабочие инструкции для AI-агентов в репозитории

## Tech Stack
- **Backend**: C# .NET 10, ASP.NET Core Web API, NLog
- **Database**: PostgreSQL 16 (EF Core / Npgsql)
- **Bot**: Telegram.Bot SDK
- **Frontend**: Vue.js 3 + Vite + TypeScript (Mini App & Admin UI)
- **Deploy**: Docker / Docker Compose, GitHub Actions CI/CD

## Development Setup

### Prerequisites
- .NET 10 SDK
- Node.js 22+
- Docker & Docker Compose
- PostgreSQL 16 (or use Docker Compose)

### Quick Start

```bash
# 0. Скопировать .env.example и заполнить переменные
cp .env.example .env

# 1. Start PostgreSQL
docker compose up -d postgres

# 2. Apply EF Core migrations (run from repository root)
# Uses DesignTimeDbContextFactory from DayTrace.Infrastructure
dotnet ef database update --project src/DayTrace.Infrastructure

# 3. Run API (from repository root)
dotnet run --project src/DayTrace.Api

# 4. Run Mini App dev server
npm --prefix src/miniapp install
npm --prefix src/miniapp run dev

# 5. Run Admin UI dev server
npm --prefix src/admin-ui install
npm --prefix src/admin-ui run dev
```

Примечание по зависимостям: в `src/DayTrace.Infrastructure/DayTrace.Infrastructure.csproj` явно закреплён `Microsoft.EntityFrameworkCore.Relational` (`10.0.3`) для совместимости с текущим `Npgsql` provider (см. фикс `8817634`).

По умолчанию:
- API при локальном запуске слушает `http://localhost:5000`
- PostgreSQL в Docker Compose доступен на хосте как `localhost:5433`
- API в Docker Compose доступен на `http://localhost:5005`

### Telegram Bot Mode

- Бот работает только через webhook endpoint `POST /bot/webhook`.
- Переменная `TELEGRAM_WEBHOOK_BASE_URL` обязательна — без неё бот не будет получать обновления.
- Поддерживаемые команды бота: `/start`, `/help` (`/settings` удалена).
- Все пользовательские настройки теперь открываются и меняются в Telegram Mini App (вкладка `Settings`).
- Публичные анонимные endpoint'ы: `POST /bot/webhook`, `GET /privacy`.

### Актуальные ограничения и поведение

- Одно основное событие на пользователя в день (`local_date`).
- API `POST /events` при попытке создать второе событие за тот же день возвращает `409` (`event_exists`) и `existing_event_id`.
- В боте повторная запись события за текущий день обновляет существующее событие, а не создаёт новое.
- Экран `Today` в Mini App оформлен как journal-страница: запись за выбранный день, inline редактирование/удаление, оценки важности и дня.
- Итоги week/month/year сейчас работают через ручной выбор `highlight`-события в Mini App (экраны `Week`/`Month`/`Year`), а не через фоновые `period_jobs`.
- API для выбора главного события периода: `PUT /summaries/{periodType}/highlight` (`periodType`: `weekly|monthly|yearly`, требуется `X-Client-Operation-Id` для dedupe).
- `GET /summaries/{periodType}` возвращает `highlight_event_id`; в `Month` выбираются weekly-highlight'ы, в `Year` — monthly-highlight'ы.

### Mini App в Telegram (актуально)

- Mini App учитывает Telegram safe area inset'ы и высоту виртуальной клавиатуры (sticky actions/нижняя навигация не перекрываются на мобильных устройствах).
- Поддерживаются Telegram theme params (`themeChanged`), Telegram `BackButton` (с очисткой callback'ов без утечек) и haptic feedback в ключевых действиях.
- Черновик текста в `Today` автосохраняется локально по дате и очищается после успешной отправки события.
- Есть вкладка `Info` с разделами о проекте/гайде/поддержке.

### Admin UI (актуально)

- В `Operations` массовая рассылка (`POST /admin/messaging/broadcast`) работает через очередь: запрос создаёт campaign + `pending` delivery attempts, а фактическая отправка выполняется фоновым `DeliveryRetryService`.
- Доступны список/детали кампаний (`GET /admin/messaging/broadcasts`, `GET /admin/messaging/broadcasts/{id}`) с агрегированными статусами доставки (`pending/sent/failed/terminal_failed`) и статусом кампании (`queued/processing/completed/...`).
- Поддерживаются аудитории рассылки `active` и `reminders`; прогресс/ошибки видны через campaigns и `GET /admin/delivery-attempts`.
- Audit log вынесен в отдельный экран (`/admin/audit-logs`) с фильтрами; логируются admin-действия, включая login/logout и операции рассылки.
- В `Content` есть admin feedback workflow: список (`GET /admin/feedback`), отметка прочитанным (`PATCH /admin/feedback/{id}/read`) и ответ пользователю (`POST /admin/feedback/{id}/reply`) с логированием в audit.
- Admin auth использует HttpOnly-cookie `daytrace_admin_session` (TTL 8 часов, `SameSite=Strict`, path `/`) с fallback на `Authorization: Bearer`.

### Runtime Workers

- API поднимает набор фоновых `HostedService` (см. `Program.cs`).
- `DeliveryRetryService` обрабатывает не только retry для `failed` delivery attempts, но и первичную отправку queued `admin_broadcast` попыток из Admin UI.
- В текущей реализации фоновые `PeriodJobWorkerService`/`StuckJobReaperService` не зарегистрированы; периодические итоги выбираются пользователем через `highlight`-flow.
- Полный список и поведение: `docs/RUNTIME_WORKERS.md`.

### Основные Dev-команды

```bash
# API with hot reload
dotnet watch run --project src/DayTrace.Api

# Run all tests (unit + integration: auth/admin/RBAC/timezone/edge cases + background services + bot)
dotnet test tests/DayTrace.Tests/DayTrace.Tests.csproj

# Stable targeted regression run (SubscriptionTests + SubscriptionServiceTests + BotUpdateHandlerTests)
./scripts/test-subscriptions-stable.sh

# Build Mini App
npm --prefix src/miniapp ci
npm --prefix src/miniapp run build

# Build Admin UI
npm --prefix src/admin-ui ci
npm --prefix src/admin-ui run build
```

### Тесты

Проект включает:
- **Интеграционные тесты** (auth, events, summaries, admin RBAC) — через `Testcontainers.PostgreSql` (реальный PostgreSQL в контейнере).
- **Unit-тесты** доменных сервисов и фоновых сервисов (`DailyReminderService`, `DeliveryRetryService`, `BotUpdateHandler`) — через Moq. Тестовый проект имеет доступ к `internal` методам API через `InternalsVisibleTo`.

Требования: Docker daemon должен быть запущен (для Testcontainers).

Если локально проявляется флап restore/build вида `*.nuget.g.props already exists`, используйте
`./scripts/test-subscriptions-stable.sh` (последовательный restore/test + очистка transient `obj`-артефактов).

### Seed Admin User

```bash
# Seed выполняется автоматически при старте API, если заданы обе переменные
export ADMIN_SEED_EMAIL=admin@example.com
export ADMIN_SEED_PASSWORD=YourSecurePassword
dotnet run --project src/DayTrace.Api
```

## Deployment (Docker Compose)

### Environment Variables

| Variable | Description | Default |
|---|---|---|
| `POSTGRES_DB` | PostgreSQL database name | `daytrace` |
| `POSTGRES_USER` | PostgreSQL user | `daytrace` |
| `POSTGRES_PASSWORD` | PostgreSQL password | `daytrace_dev` |
| `TELEGRAM_BOT_TOKEN` | Telegram Bot API token | (required) |
| `TELEGRAM_WEBHOOK_BASE_URL` | Public base URL for webhook mode (required for bot updates) | empty |
| `TELEGRAM_WEBHOOK_SECRET` | Webhook secret token (`X-Telegram-Bot-Api-Secret-Token`) | empty |
| `TELEGRAM_MINIAPP_URL` | Public Mini App URL for bot buttons/reminders (falls back to webhook base URL if empty) | empty |
| `ALLOWED_ORIGINS` | CORS allowed origins (обязательный, comma-separated, без wildcard `*`) | (required) |
| `ADMIN_SEED_EMAIL` | Email для стартового admin-пользователя (только при первичном/явном seed) | empty |
| `ADMIN_SEED_PASSWORD` | Пароль для стартового admin-пользователя | empty |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Production` |

### Build & Run

```bash
# Build and start all services
docker compose up -d --build

# View logs
docker compose logs -f api

# Stop
docker compose down
```

### Services

| Service | Port | Description |
|---|---|---|
| `postgres` | 5433 (container 5432) | PostgreSQL database |
| `api` | 5005 (container 8080) | .NET API + Bot + Mini App static files |
| `admin-ui` | 5174 | Admin dashboard (nginx) |

### Health Checks

- API: `GET http://localhost:5005/health` — возвращает `{ status, version, timestamp }`. Поле `version` берётся из `AssemblyInformationalVersion` (при деплое через git-тег — версия из тега, иначе `dev`).
- API + DB readiness: `GET http://localhost:5005/health/db`
- PostgreSQL: `pg_isready` built-in

### Mini App Static Files & Cache

- API раздаёт production-сборку Mini App из `src/miniapp/dist`.
- Файлы в `/assets/*` (хешированные JS/CSS) отдаются с долгим cache (`max-age=31536000`, `immutable`).
- `index.html` и корневые файлы отдаются с `no-cache/no-store`, чтобы клиент быстро подхватывал новую версию приложения.

### CI/CD и версионирование

- Деплой автоматизирован через GitHub Actions: при пуше git-тега `v*.*.*` workflow подключается к серверу по SSH и запускает `scripts/deploy.sh`.
- Версия из тега встраивается в .NET assembly (`GET /health`), в Mini App и Admin UI (через `VITE_APP_VERSION`).
- Ручной деплой возможен через `workflow_dispatch` в GitHub Actions UI.
- Deploy-скрипт выполняет health check с retry (до 60 секунд) после запуска контейнеров.
- Подробная инструкция: `docs/deploy-ci-cd.md`.

```bash
# Деплой через git-тег
git tag v1.2.3
git push origin v1.2.3
```

### Безопасность

- Admin session cookie (`daytrace_admin_session`) автоматически устанавливает флаг `Secure` при работе через HTTPS или за reverse proxy (`X-Forwarded-Proto: https`).
- Конфиденциальные переменные (токены, пароли) вынесены в `.env` (файл в `.gitignore`). Шаблон: `.env.example`.
- `POST /auth/dev` доступен только в `ASPNETCORE_ENVIRONMENT=Development`.

### Local Tunnel (Telegram Dev)

- Скрипт `./start-tunnel.sh [port]` поднимает `cloudflared`-туннель к локальному API (по умолчанию `5005`).
- Скрипт обновляет `.env`: `TELEGRAM_WEBHOOK_BASE_URL` и `TELEGRAM_MINIAPP_URL`, затем регистрирует Telegram webhook и обновляет кнопку открытия Mini App.
