# Задача: Настройка CI/CD с версионированием для проекта DayTrace

## Контекст проекта

DayTrace — Telegram Bot + Mini App + Admin Dashboard.
- **Backend:** .NET 10, ASP.NET Core Web API (проект `DayTrace.Api`)
- **Frontend:** Vue 3 + Vite + TypeScript (2 приложения: `src/miniapp`, `src/admin-ui`)
- **БД:** PostgreSQL 16
- **Контейнеризация:** Docker Compose (3 сервиса: postgres, api, admin-ui)
- **Сервер:** Ubuntu 24.04, IP 5.181.3.45, пользователь root, путь `/opt/daytrace`
- **Reverse proxy:** Nginx (SSL termination, Let's Encrypt)
- **Домен:** daytrace.duckdns.org
- **Репозиторий:** https://github.com/DadonovAleksandr/day-trace.git

### Архитектура

```
DayTrace.Domain          → Сущности, интерфейсы (нет зависимостей от фреймворков)
DayTrace.Infrastructure  → EF Core DbContext, репозитории, миграции
DayTrace.Bot             → Telegram Bot handlers
DayTrace.Api             → Контроллеры, middleware, background services, DI
```

Общие настройки .NET в `Directory.Build.props`: `net10.0`, `Nullable=enable`.

### Docker

- `Dockerfile.api` — multi-stage: (1) node:22-alpine собирает miniapp, (2) dotnet/sdk:10.0 publish API, (3) dotnet/aspnet:10.0 runtime. Miniapp раздаётся как static files из API.
- `Dockerfile.frontend` — multi-stage: (1) node:22-alpine собирает Vue app (ARG APP_DIR), (2) nginx:alpine.
- `docker-compose.yml` — dev-конфиг с 3 сервисами (postgres, api, admin-ui).
- На сервере используется `docker-compose.prod.yml` (отдельный файл, не в git).

### Текущий деплой (ручной)

Проект загружается на сервер, собирается через `docker compose -f docker-compose.prod.yml build`, запускается через `up -d`. Миграции применяются вручную. Нет CI/CD.

### Существующие эндпоинты

- `GET /health` → `{ status: "healthy", timestamp: "..." }` (файл: `src/DayTrace.Api/Controllers/HealthController.cs`)
- `GET /health/db` — проверка БД

### Экран "О проекте" в Mini App

В `src/miniapp/src/views/InfoView.vue` есть экран с секциями: "О проекте", "Инструкция", "Оплата", "Связаться с нами". Это идеальное место для отображения версии приложения.

---

## Требования

### 1. Триггер — теги версий

- CI/CD workflow запускается при создании git-тега формата `v*.*.*` (например, `v1.0.0`, `v1.2.3`).
- Также должна быть возможность ручного запуска (`workflow_dispatch`) с вводом версии.
- Workflow НЕ должен запускаться на push в main.

### 2. Автоматическое встраивание версии в код

Версия из git-тега (без префикса `v`) должна автоматически попадать в:

#### Backend (.NET)
- Установить `Version`, `AssemblyVersion`, `InformationalVersion` в сборку.
  Рекомендуемый подход: передавать версию через `dotnet publish -p:Version=X.Y.Z` или генерировать файл `Directory.Build.props` / отдельный `Version.props`.
- Добавить эндпоинт `GET /health` (расширить существующий) или `GET /version`, который возвращает версию из Assembly. Пример:
  ```csharp
  var version = Assembly.GetExecutingAssembly()
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
      ?.InformationalVersion ?? "unknown";
  ```
  Добавить поле `version` в ответ `/health`: `{ status: "healthy", version: "1.2.3", timestamp: "..." }`.

#### Frontend (miniapp)
- Передать версию как переменную окружения Vite: `VITE_APP_VERSION`.
  В `Dockerfile.api` (stage 1 — сборка miniapp) передать build arg `APP_VERSION`, затем `ENV VITE_APP_VERSION=$APP_VERSION` перед `npm run build`.
- В коде miniapp доступ через `import.meta.env.VITE_APP_VERSION`.

#### Frontend (admin-ui)
- Аналогично miniapp: build arg → `VITE_APP_VERSION`.

### 3. Отображение версии в UI

- На экране "О проекте" (`src/miniapp/src/views/InfoView.vue`) добавить отображение версии приложения. Разместить внизу экрана, мелким шрифтом (hint color), формат: `Версия 1.2.3`. Версию брать из `import.meta.env.VITE_APP_VERSION`, с fallback на `'dev'` если не задана.

### 4. GitHub Actions Workflow

Создать `.github/workflows/deploy.yml`:

```yaml
name: Deploy DayTrace

on:
  push:
    tags:
      - 'v*.*.*'
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to deploy (e.g. 1.2.3)'
        required: true
```

#### Шаги workflow:

1. **Checkout** — `actions/checkout@v4`
2. **Извлечение версии** — из тега (`${GITHUB_REF#refs/tags/v}`) или из input.
3. **Подключение к серверу по SSH** — использовать `appleboy/ssh-action@v1`.
4. **На сервере выполнить deploy-скрипт**, передавая версию как аргумент.

#### GitHub Secrets (нужно настроить вручную):
- `DEPLOY_SSH_KEY` — приватный SSH-ключ ed25519
- `DEPLOY_HOST` — `5.181.3.45`
- `DEPLOY_USER` — `root`

### 5. Deploy-скрипт на сервере

Создать `/opt/daytrace/deploy.sh` (добавить в репозиторий как `scripts/deploy.sh`):

```bash
#!/bin/bash
set -euo pipefail

VERSION="${1:?Usage: deploy.sh <version>}"
DEPLOY_DIR="/opt/daytrace"
LOG_FILE="$DEPLOY_DIR/deploy.log"

log() { echo "[$(date -u '+%Y-%m-%d %H:%M:%S UTC')] $*" | tee -a "$LOG_FILE"; }

cd "$DEPLOY_DIR"

log "=== Deploying version $VERSION ==="
log "Pulling latest code..."
git fetch --all --tags
git checkout "v$VERSION"

log "Building containers with version $VERSION..."
APP_VERSION=$VERSION docker compose -f docker-compose.prod.yml build

log "Starting containers..."
APP_VERSION=$VERSION docker compose -f docker-compose.prod.yml up -d

log "Pruning old images..."
docker image prune -f

log "Checking health..."
sleep 10
if curl -sf http://localhost:8080/health | grep -q "healthy"; then
  log "Deploy v$VERSION successful"
else
  log "Health check failed!"
  exit 1
fi
```

### 6. Подготовка сервера для git pull

Сейчас на сервере проект загружен без .git. Нужно:
- Инициализировать git-репозиторий в `/opt/daytrace`.
- Добавить remote origin.
- Настроить `.gitignore` на сервере: `.env`, `backups/`, `deploy.log`.
- Production-файлы (`docker-compose.prod.yml`, `.env`, `backup-db.sh`) не должны перезатираться — они не в git.

**Предоставить инструкцию** для ручной подготовки сервера (одноразовая операция).

### 7. Передача версии в Docker

Обновить `Dockerfile.api`:
- Добавить `ARG APP_VERSION=dev` в каждый stage где нужно.
- В stage 1 (miniapp build): `ARG APP_VERSION` → `ENV VITE_APP_VERSION=$APP_VERSION` перед `npm run build`.
- В stage 2 (.NET build): `ARG APP_VERSION` → передать в `dotnet publish -p:Version=$APP_VERSION`.
- В stage 3 (runtime): не нужно, версия уже в сборке.

Обновить `Dockerfile.frontend` (admin-ui):
- Добавить `ARG APP_VERSION=dev` → `ENV VITE_APP_VERSION=$APP_VERSION` перед `npm run build`.

Обновить `docker-compose.yml` (dev):
- Добавить `args: APP_VERSION: ${APP_VERSION:-dev}` в build секции api и admin-ui.

---

## Важные ограничения

- `.env` файл с секретами НЕ должен храниться в git.
- `docker-compose.prod.yml` живёт только на сервере и не должен быть в репозитории.
- Миграции БД НЕ применять автоматически (только вручную).
- При ошибке сборки контейнеры не должны останавливаться (сначала build, потом up).
- Тесты: `dotnet test` (xUnit + Testcontainers — требует Docker). Запускать тесты в CI перед деплоем НЕ обязательно (Testcontainers требуют Docker-in-Docker, это усложняет пайплайн). Можно добавить отдельный CI workflow для тестов позже.
- Код пиши максимально качественно, применяя SOLID.
- Не добавляй лишних фич и абстракций — только то, что описано в требованиях.

## Ожидаемый результат

### Файлы для создания/изменения:

1. `.github/workflows/deploy.yml` — GitHub Actions workflow
2. `scripts/deploy.sh` — deploy-скрипт для сервера
3. `src/DayTrace.Api/Controllers/HealthController.cs` — добавить поле `version` в ответ
4. `src/miniapp/src/views/InfoView.vue` — отображение версии
5. `Dockerfile.api` — добавить `ARG APP_VERSION`, передать в miniapp и dotnet publish
6. `Dockerfile.frontend` — добавить `ARG APP_VERSION`, передать в Vite
7. `docker-compose.yml` — добавить `APP_VERSION` build arg
8. `docs/deploy-ci-cd.md` — инструкция по настройке: GitHub Secrets, подготовка сервера (git init), первый деплой

### Ожидаемый флоу:

1. Разработчик делает изменения, пушит в main.
2. Когда готов к релизу — создаёт тег: `git tag v1.2.3 && git push origin v1.2.3`.
3. GitHub Actions подхватывает тег, подключается к серверу по SSH.
4. На сервере: `git checkout v1.2.3`, сборка образов с версией, перезапуск контейнеров.
5. `GET /health` возвращает `{ version: "1.2.3", ... }`.
6. Экран "О проекте" в Mini App показывает "Версия 1.2.3".
