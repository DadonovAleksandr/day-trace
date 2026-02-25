# Настройка GitHub Actions CI/CD для автодеплоя DayTrace

Настрой автоматический деплой приложения DayTrace на production-сервер через GitHub Actions. При пуше в ветку `main` должен запускаться workflow, который подключается к серверу по SSH и выполняет обновление.

## Инфраструктура

- **GitHub-репозиторий:** https://github.com/DadonovAleksandr/day-trace.git
- **Ветка деплоя:** main
- **Сервер:** 5.181.3.45, Ubuntu 24.04, пользователь root
- **Проект на сервере:** /opt/daytrace/
- **Docker Compose файл:** docker-compose.prod.yml
- **SSH-доступ:** по ключу ed25519 (уже настроен)

## Что нужно сделать

### 1. Создать deploy-скрипт на сервере `/opt/daytrace/deploy.sh`

Скрипт должен:

- Перейти в /opt/daytrace
- Выполнить `git pull origin main`
- Пересобрать Docker-образы (`docker compose -f docker-compose.prod.yml build`)
- Перезапустить контейнеры (`docker compose -f docker-compose.prod.yml up -d`)
- Удалить неиспользуемые Docker-образы (`docker image prune -f`)
- Логировать результат деплоя с timestamp в /opt/daytrace/deploy.log

### 2. Подготовить сервер для git pull

Сейчас проект загружен на сервер через tar+ssh (без .git). Нужно:

- Инициализировать git-репозиторий в /opt/daytrace
- Настроить remote origin на https://github.com/DadonovAleksandr/day-trace.git
- Настроить .gitignore чтобы не трекать .env, backups/, deploy.log
- Сделать первый pull/checkout ветки main
- Убедиться что .env и другие production-файлы (docker-compose.prod.yml, backup-db.sh, deploy.sh) не перезатираются при git pull

### 3. Создать GitHub Actions workflow `.github/workflows/deploy.yml`

Workflow должен:

- Триггериться на push в `main`
- Иметь возможность ручного запуска (workflow_dispatch)
- Подключаться к серверу по SSH
- Выполнять /opt/daytrace/deploy.sh
- Использовать GitHub Secrets для хранения SSH-ключа и IP сервера

Необходимые GitHub Secrets:

- `DEPLOY_SSH_KEY` — приватный SSH-ключ (ed25519)
- `DEPLOY_HOST` — IP сервера (5.181.3.45)
- `DEPLOY_USER` — пользователь (root)

Использовать action `appleboy/ssh-action@v1` для SSH-подключения.

### 4. Настроить GitHub Secrets

Помоги сгенерировать отдельную пару SSH-ключей специально для деплоя (deploy key), добавить публичный ключ на сервер в authorized_keys, и объяснить какие секреты прописать в GitHub Settings → Secrets.

## Важно

- .env файл с секретами НЕ должен храниться в git-репозитории
- docker-compose.prod.yml, deploy.sh, backup-db.sh, migrate.sh — это production-файлы, которые живут только на сервере и не должны перезатираться при git pull
- Миграции БД НЕ применять автоматически (только вручную через migrate.sh)
- При ошибке сборки контейнеры не должны останавливаться (сначала build, потом up)
- Workflow должен отправлять статус деплоя (успех/ошибка)
