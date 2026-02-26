# CI/CD и версионирование DayTrace

## Обзор

Деплой DayTrace автоматизирован через GitHub Actions: при пуше git-тега формата `v*.*.*` workflow подключается к серверу по SSH и запускает deploy-скрипт. Версия из тега встраивается в .NET assembly (отображается в `GET /health`) и во Vue-фронтенды через `VITE_APP_VERSION` (отображается на экране «О проекте» в Mini App).

Ручной деплой также возможен через `workflow_dispatch` с указанием версии.

## Предварительные требования

- Доступ к GitHub-репозиторию [DadonovAleksandr/day-trace](https://github.com/DadonovAleksandr/day-trace)
- Сервер Ubuntu 24.04 с установленными: Docker, docker compose, git, curl
- SSH-ключевая пара ed25519 для подключения GitHub Actions к серверу

## Настройка GitHub Secrets

Для работы workflow необходимо настроить три секрета в репозитории:

| Секрет | Значение | Описание |
|--------|----------|----------|
| `DEPLOY_SSH_KEY` | Приватный ключ ed25519 | SSH-ключ для подключения к серверу |
| `DEPLOY_HOST` | `5.181.3.45` | IP-адрес сервера |
| `DEPLOY_USER` | `root` | Пользователь для SSH-подключения |

**Как добавить секреты:**

1. Открыть репозиторий на GitHub
2. Перейти в **Settings** → **Secrets and variables** → **Actions**
3. Нажать **New repository secret**
4. Ввести имя секрета и его значение
5. Повторить для каждого секрета

## Подготовка сервера (одноразовая операция)

Выполнить на сервере от имени `root`:

```bash
cd /opt/daytrace

# Инициализация git-репозитория
git init
git remote add origin https://github.com/DadonovAleksandr/day-trace.git
git fetch --all --tags

# Переключение на нужный тег (пример)
git checkout v1.0.0

# Файлы docker-compose.prod.yml, .env и backup-db.sh
# НЕ хранятся в git — они не будут перезатёрты при checkout

# Настройка .gitignore на сервере (чтобы git status не показывал production-файлы)
cat >> .gitignore << 'EOF'
.env
backups/
deploy.log
EOF

# Копирование deploy-скрипта из репозитория и установка прав
cp scripts/deploy.sh /opt/daytrace/deploy.sh
chmod +x /opt/daytrace/deploy.sh

# Добавление публичного SSH-ключа для GitHub Actions
# Вставить публичный ключ (соответствующий DEPLOY_SSH_KEY) в файл:
#   ~/.ssh/authorized_keys
```

## Как выполнить деплой

### Через git-тег (основной способ)

```bash
git tag v1.2.3
git push origin v1.2.3
```

GitHub Actions автоматически подхватит тег и выполнит деплой на сервер.

### Ручной запуск через GitHub UI

1. Перейти в **Actions** → **Deploy DayTrace**
2. Нажать **Run workflow**
3. Ввести версию без префикса `v` (например, `1.2.3`)
4. Нажать **Run workflow**

## Как работает версионирование

Последовательность встраивания версии при деплое:

1. **Извлечение версии** — GitHub Actions извлекает версию из git-тега (`v1.2.3` → `1.2.3`) или из ручного ввода (`workflow_dispatch`).
2. **Передача в Docker** — версия передаётся как build arg `APP_VERSION` в docker compose.
3. **Сборка API** (`Dockerfile.api`):
   - **Stage 1 (miniapp):** `ARG APP_VERSION` → `ENV VITE_APP_VERSION=$APP_VERSION` → `npm run build`. Mini App получает версию через `import.meta.env.VITE_APP_VERSION`.
   - **Stage 2 (.NET):** `ARG APP_VERSION` → `dotnet publish -p:Version=$APP_VERSION`. Версия встраивается в `AssemblyInformationalVersion` сборки.
4. **Сборка admin-ui** (`Dockerfile.frontend`): `ARG APP_VERSION` → `ENV VITE_APP_VERSION=$APP_VERSION` → `npm run build`. Admin UI получает версию аналогично miniapp.
5. **Результат:**
   - `GET /health` возвращает `{ "status": "healthy", "version": "1.2.3", "timestamp": "..." }`
   - Экран «О проекте» в Mini App отображает «Версия 1.2.3»

## Структура файлов

| Файл | Назначение |
|------|-----------|
| `.github/workflows/deploy.yml` | GitHub Actions workflow (триггер на теги + ручной запуск) |
| `scripts/deploy.sh` | Скрипт деплоя, выполняется на сервере по SSH |
| `Dockerfile.api` | Multi-stage сборка: miniapp (node) + API (.NET) + runtime (с версией) |
| `Dockerfile.frontend` | Multi-stage сборка: Vue app (node) + nginx (с версией) |
| `docker-compose.yml` | Dev-конфиг с `APP_VERSION` build arg (fallback `dev`) |

## Проверка деплоя

```bash
# На сервере — проверка health endpoint
curl -s http://localhost:8080/health | python3 -m json.tool
```

Ожидаемый ответ:

```json
{
    "status": "healthy",
    "version": "1.2.3",
    "timestamp": "2026-02-26T12:00:00Z"
}
```

## Логи деплоя

Лог деплоя записывается в файл `/opt/daytrace/deploy.log` на сервере. Каждая запись содержит UTC-timestamp и описание шага:

```
[2026-02-26 12:00:00 UTC] === Deploying version 1.2.3 ===
[2026-02-26 12:00:01 UTC] Pulling latest code...
[2026-02-26 12:00:05 UTC] Building containers with version 1.2.3...
[2026-02-26 12:01:30 UTC] Starting containers...
[2026-02-26 12:01:35 UTC] Pruning old images...
[2026-02-26 12:01:47 UTC] Deploy v1.2.3 successful
```

## Устранение неполадок

| Проблема | Диагностика |
|----------|-------------|
| **SSH connection failed** | Проверить секрет `DEPLOY_SSH_KEY` (приватный ключ ed25519), наличие публичного ключа в `~/.ssh/authorized_keys` на сервере, открытость порта 22 в firewall |
| **Health check failed** | Проверить логи контейнера: `docker compose -f docker-compose.prod.yml logs api` |
| **Docker build failed** | Просмотреть вывод сборки в deploy.log или запустить build вручную: `APP_VERSION=1.2.3 docker compose -f docker-compose.prod.yml build` |
| **Tag not found on server** | Убедиться, что тег запушен в remote: `git tag -l` (локально) и `git ls-remote --tags origin` |
| **Версия отображается как "dev"** | Проверить, что `APP_VERSION` передаётся при сборке: `docker inspect daytrace-api` → проверить labels/env |
