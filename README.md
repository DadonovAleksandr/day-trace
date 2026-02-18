# DayTrace

DayTrace — сервис личных заметок и рефлексии с иерархией итогов:
**день → неделя → месяц → год**.

## MVP
- Telegram Bot
- Telegram Mini App
- Web Admin UI

## Документация
- `PRD.md` — продуктовые требования
- `METRICS.md` — спецификация метрик

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

# 2. Apply EF Core migrations
cd src/DayTrace.Api
dotnet ef database update

# 3. Run the API
dotnet run --project src/DayTrace.Api

# 4. Run Mini App dev server
cd src/miniapp && npm install && npm run dev

# 5. Run Admin UI dev server
cd src/admin-ui && npm install && npm run dev
```

### Seed Admin User

```bash
# Via CLI (run from project root after API is built)
dotnet run --project src/DayTrace.Api -- seed-admin --email admin@example.com --password YourSecurePassword
```

## Deployment (Docker Compose)

### Environment Variables

| Variable | Description | Default |
|---|---|---|
| `POSTGRES_PASSWORD` | PostgreSQL password | `daytrace_dev` |
| `TELEGRAM_BOT_TOKEN` | Telegram Bot API token | (required) |
| `TELEGRAM_WEBHOOK_SECRET` | Webhook secret token | (required) |
| `TELEGRAM_WEBHOOK_BASE_URL` | Public URL for webhook | (required) |
| `ALLOWED_ORIGINS` | CORS allowed origins | `*` |
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
| `api` | 5000 | .NET API + Bot (webhook) |
| `miniapp` | 5173 | Telegram Mini App (nginx) |
| `admin-ui` | 5174 | Admin dashboard (nginx) |

### Health Checks

- API: `GET http://localhost:5000/health`
- PostgreSQL: `pg_isready` built-in
