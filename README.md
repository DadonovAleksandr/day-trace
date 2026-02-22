# DayTrace

DayTrace — сервис личных заметок и рефлексии с иерархией итогов:
**день → неделя → месяц → год**.

## MVP
- Telegram Bot
- Telegram Mini App
- Web Admin UI

## Документация
- `docs/README.md` — индекс документации
- `PRD.md` — продуктовые требования
- `METRICS.md` — спецификация метрик
- `CLAUDE.md` — правила и рабочие инструкции для AI-агентов в репозитории

## Tech Stack
- **Backend**: C# .NET 9, ASP.NET Core Web API, NLog
- **Database**: PostgreSQL 16 (EF Core / Npgsql)
- **Bot**: Telegram.Bot SDK
- **Frontend**: Vue.js 3 + Vite + TypeScript (Mini App & Admin UI)
- **Deploy**: Docker / Docker Compose

## Development Setup

### Prerequisites
- .NET 9 SDK
- Node.js 22+
- Docker & Docker Compose
- PostgreSQL 16 (or use Docker Compose)

### Quick Start

```bash
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

### Telegram Bot Mode

- Бот работает только через webhook endpoint `POST /bot/webhook`.
- Переменная `TELEGRAM_WEBHOOK_BASE_URL` обязательна — без неё бот не будет получать обновления.

### Runtime Workers

- API поднимает набор фоновых `HostedService` (см. `Program.cs`).
- Полный список и поведение: `docs/RUNTIME_WORKERS.md`.

### Основные Dev-команды

```bash
# API with hot reload
dotnet watch run --project src/DayTrace.Api

# Run all tests (unit + integration: auth/admin/RBAC/timezone/edge cases)
dotnet test tests/DayTrace.Tests/DayTrace.Tests.csproj

# Build Mini App
npm --prefix src/miniapp ci
npm --prefix src/miniapp run build

# Build Admin UI
npm --prefix src/admin-ui ci
npm --prefix src/admin-ui run build
```

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
| `TELEGRAM_WEBHOOK_BASE_URL` | Public base URL for webhook mode | empty (`polling` mode) |
| `TELEGRAM_WEBHOOK_SECRET` | Webhook secret token (`X-Telegram-Bot-Api-Secret-Token`) | empty |
| `ALLOWED_ORIGINS` | CORS allowed origins | `*` |
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
| `postgres` | 5432 | PostgreSQL database |
| `api` | 5005 (container 8080) | .NET API + Bot (polling/webhook) |
| `miniapp` | 5173 | Telegram Mini App (nginx) |
| `admin-ui` | 5174 | Admin dashboard (nginx) |

### Health Checks

- API: `GET http://localhost:5005/health`
- API + DB readiness: `GET http://localhost:5005/health/db`
- PostgreSQL: `pg_isready` built-in
