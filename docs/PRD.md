# PRD v3.0 — MVP (Current State)
## Сервис фиксации главных событий (Telegram Bot + Telegram Mini App + Web Admin UI)

> Статус документа: актуальные продуктовые требования и текущий MVP-контракт (без архивных разделов).
> Для commit-level изменений и release notes используйте `docs/IMPLEMENTATION_STATUS.md`.

## 1) Цель MVP

Дать пользователю простой и регулярный процесс фиксации важных событий и рефлексии по периодам:
**день → неделя → месяц → год**.

Ключевые принципы MVP:
- понятный ежедневный flow в Telegram;
- одна запись события на локальный день;
- прозрачные правила периодов (timezone / week_end);
- ручной выбор главного события периода вместо авто-генерации;
- наблюдаемая admin-операционка (delivery, audit, feedback, campaigns).

## 2) Scope MVP

### Входит

1. Telegram Bot
- webhook-only режим;
- команды `/start`, `/help`;
- ввод/обновление события дня;
- напоминания;
- кнопка открытия Mini App.

2. Telegram Mini App (основной пользовательский UI)
- экраны `Today`, `Week`, `Month`, `Year`, `Settings`, `Info`;
- работа с событиями, рейтингом дня и периодическими итогами через highlight-flow;
- Telegram-native UX (safe area, theme, back button, haptics).

3. Web Admin UI
- Dashboard (метрики);
- Users;
- Content (events, summaries, feedback);
- Operations (delivery attempts, broadcast campaigns);
- Audit.

### Не входит (MVP out of scope)

- AI-саммаризация и генерация текста итогов;
- нативные мобильные приложения;
- командные аккаунты/шаринг;
- расширенная BI-аналитика beyond dashboard;
- синхронная массовая рассылка из admin UI (в текущем MVP рассылка только через queue/campaign flow).

## 3) Product Requirements (Current)

## FR-0. Регистрация и вход пользователя

Пользователь создаётся при первом взаимодействии через Telegram Bot или при первом успешном `POST /auth/telegram` из Mini App.

Требования:
- идентичность пользователя определяется Telegram account (`telegram_user_id`);
- повторные входы не создают дубль пользователя;
- при первом входе создаются настройки по умолчанию и служебные записи истории (`timezone_history`, `week_schedule_history`);
- если пользователь начал через Bot без Mini App, допустим fallback timezone и последующая настройка в `Settings`.

## FR-1. User Auth (Mini App + Bot)

### FR-1.1 Mini App auth handshake

- Mini App использует `POST /auth/telegram`.
- Backend валидирует Telegram init data (подпись / корректность данных) и выдает user session token.
- Используется replay protection для защиты от повторного использования init data.
- Все user API endpoints (кроме публичных) требуют валидную user session.

### FR-1.2 Bot webhook auth

- Bot работает только через `POST /bot/webhook`.
- Входящий webhook проверяется через `X-Telegram-Bot-Api-Secret-Token`.
- Без корректного webhook secret входящий трафик не считается доверенным.

## FR-2. Событие дня (core flow)

Событие дня — базовая пользовательская сущность.

Требования:
- поля: `text`, `importance`, `local_date`;
- `text`: 1..500 символов;
- `importance`: 1..5;
- одна запись события на пользователя в один `local_date`;
- при попытке создать второе событие за день API возвращает `409 event_exists` с `existing_event_id`;
- поддерживаются create/list/edit/delete через API и Mini App/Bot;
- удаление soft-delete (`deleted_at`), удалённые записи не должны попадать в пользовательский UI и расчёты периодов;
- редактирование/удаление разрешено до момента блокировки периодом: backend использует `EventLockService` и запрещает изменения, если событие попало в заблокированный weekly summary-flow.

## FR-3. Периоды и summaries (manual highlight flow)

Периодические итоги в текущем MVP работают через ручной выбор главного события периода (`highlight_event_id`).

### FR-3.1 Общие правила

- Поддерживаются типы периодов: `weekly`, `monthly`, `yearly`.
- Текущий основной endpoint изменения итога периода: `PUT /summaries/{periodType}/highlight`.
- `GET /summaries/{periodType}` возвращает summary записи, включая `highlight_event_id`.
- Операция выбора highlight должна быть идемпотентной на уровне client action (через `X-Client-Operation-Id`).
- Выбор highlight возможен только для событий пользователя и только в допустимых границах периода.

### FR-3.2 Ограничения выбора по уровням периода

- `Week`: пользователь выбирает одно событие из событий недели.
- `Month`: пользователь выбирает одно weekly-highlight событие месяца.
- `Year`: пользователь выбирает одно monthly-highlight событие года.

### FR-3.3 Актуальный UX

- В Mini App `Week/Month/Year` используется selection-first flow (выбор и сохранение highlight), а не запуск фоновой генерации периода.
- Старые механики авто-генерации period jobs отсутствуют в текущем продукте и не являются частью текущего PRD.

## FR-4. Timezone и настройки пользователя

### FR-4.1 Timezone как источник истины

- Все пользовательские даты и границы периодов считаются в IANA timezone пользователя.
- `local_date` — дата в TZ пользователя.
- Смена timezone сохраняется в истории (`timezone_history`) и влияет на новые вычисления периодов/напоминаний.

### FR-4.2 Настройки (`GET/PUT /settings`)

Пользователь может управлять:
- `timezone`;
- `reminder_time`;
- `reminder_enabled`;
- `week_end`;
- дополнительными пользовательскими флагами/предпочтениями, поддерживаемыми текущим UI/API.

Ограничения:
- backend валидирует timezone;
- смена timezone ограничена cooldown-правилом;
- смена `week_end` проходит через историю расписания недель и проверки незавершённого transition периода.

## FR-5. Day Rating

Пользователь может выставлять оценку дня (1..5) через `GET/PUT /day-rating`.

Требования:
- рейтинг хранится отдельно от события дня;
- допускается обновление существующей оценки;
- для даты применяются ограничения backend (диапазон последних дней, проверка формата);
- данные используются в пользовательском UX и admin content review.

## FR-6. Ежедневные напоминания и Telegram delivery

### FR-6.1 Напоминания

- Если `reminder_enabled=true`, система планирует ежедневное напоминание по `reminder_time` в timezone пользователя.
- Напоминания отправляются Telegram-ботом.
- Повторы отправки при сбоях управляются через `delivery_attempts` и `DeliveryRetryService`.

### FR-6.2 Delivery tracking

- Все попытки доставки Telegram-сообщений фиксируются в `delivery_attempts` со статусами (`pending`, `sent`, `failed`, `terminal_failed`).
- Повторные попытки отправки ограничены и выполняются с backoff для retryable ошибок.
- Данные используются для Admin UI (`Operations`) и метрик/наблюдаемости.

## FR-7. Telegram Bot UX

Требования к Bot UX:
- команды `/start`, `/help`;
- ввод события дня;
- при повторной записи за текущий день бот обновляет существующее событие вместо создания дубля;
- настройки пользователя открываются через Mini App (вместо bot-команды `/settings`);
- публичные анонимные endpoints, связанные с ботом/приватностью: `POST /bot/webhook`, `GET /privacy`.

## FR-8. Telegram Mini App UX

Mini App — основной пользовательский интерфейс.

### FR-8.1 Обязательные экраны

- `Today`
- `Week`
- `Month`
- `Year`
- `Settings`
- `Info`

### FR-8.2 UX/Platform integration

Требования:
- Telegram safe area inset'ы;
- учет высоты виртуальной клавиатуры для sticky UI;
- поддержка Telegram theme params (`themeChanged`);
- поддержка Telegram `BackButton` без утечек callback'ов;
- haptic feedback в ключевых действиях;
- автосохранение черновика записи дня по дате и очистка после успешной отправки.

## FR-9. Admin Auth и RBAC

### FR-9.1 Admin auth flow

- `POST /admin/auth/login` — вход администратора;
- `GET /admin/auth/me` — текущая сессия;
- `POST /admin/auth/logout` — выход;
- текущая реализация использует HttpOnly-cookie `daytrace_admin_session` и поддерживает fallback `Authorization: Bearer`.

### FR-9.2 RBAC

Роли: `admin`, `operator`, `analyst`.

Требования:
- доступ к admin endpoints проверяется сервером;
- `analyst` имеет ограничения на чувствительные операции/PII (например messaging/feedback management, часть content views);
- audit-доступ (`/admin/audit-logs`) ограничен ролью `admin`.

## FR-10. Admin UI: metrics / content / operations / audit

### FR-10.1 Dashboard

Admin dashboard предоставляет базовые продуктовые метрики:
- DAU / WAU / MAU;
- reminder conversion;
- Prompt→Summary conversion в текущей реализации временно отключена (возвращается `0/0`) до утверждения новой формулы для highlight-based flow.

Источник формул и SQL-проверок: `docs/METRICS.md`.

### FR-10.2 Content

Admin Content позволяет:
- просматривать события пользователей (с учётом RBAC и ограничений на PII);
- просматривать summaries;
- работать с feedback (список, mark-as-read, reply в Telegram).

Feedback workflow (admin-side):
- `GET /admin/feedback` — список с фильтрами;
- `PATCH /admin/feedback/{id}/read` — отметить прочитанным;
- `POST /admin/feedback/{id}/reply` — ответ пользователю через Telegram + запись delivery attempt + audit.

### FR-10.3 Operations

Admin Operations включает:
- `GET /admin/delivery-attempts` — мониторинг статусов доставки;
- массовые рассылки через broadcast campaigns;
- просмотр статусов кампаний и агрегированных delivery counters.

### FR-10.4 Audit

- `GET /admin/audit-logs` — журнал admin-действий с фильтрацией;
- логируются auth-события и операционные действия (включая рассылки и действия с feedback);
- retention audit логов поддерживается отдельным cleanup worker'ом.

## FR-11. Admin Messaging (queue-based broadcast campaigns)

Массовая рассылка в текущем MVP работает как очередь кампаний, а не синхронная отправка в HTTP-запросе.

Требования:
- `POST /admin/messaging/broadcast` создаёт campaign и `pending` `delivery_attempts`, но не обязан завершить фактическую отправку в рамках HTTP-ответа;
- поддерживаемые аудитории кампании: `active`, `reminders`;
- отправка выполняется асинхронно через `DeliveryRetryService`;
- доступны endpoint'ы списка и деталей кампаний:
  - `GET /admin/messaging/broadcasts`
  - `GET /admin/messaging/broadcasts/{id}`
- UI/API показывают агрегированные статусы доставки (`pending`, `sent`, `failed`, `terminal_failed`) и производный статус кампании.

## FR-12. Runtime Workers (API HostedService)

Актуальные фоновые сервисы API:
- `BotWebhookSetupService` (best-effort setup webhook при старте, если настроен bot token);
- `OperationIdCleanupService` (cleanup dedupe cache);
- `DailyReminderService` (ежедневные напоминания);
- `DeliveryRetryService` (retry + queue drain для `admin_broadcast`);
- `UserPurgeService` (hard-delete PII после retention окна);
- `AuditLogCleanupService` (retention cleanup audit logs).

Требования:
- в текущем MVP нет period job workers;
- интервалы/лимиты воркеров могут быть захардкожены в коде;
- эксплуатационная документация по воркерам ведётся в `docs/RUNTIME_WORKERS.md`.

## 4) API Contract (Minimum Current Surface)

Общие правила:
- JSON в `snake_case`;
- user endpoints требуют user session (кроме публичных);
- admin endpoints требуют admin auth + RBAC;
- операции изменения, чувствительные к double-submit, используют dedupe (`X-Client-Operation-Id` там, где требуется текущим API/middleware).

### 4.1 Public / platform endpoints

- `GET /health`
- `GET /health/db`
- `GET /privacy`
- `POST /bot/webhook`
- `POST /auth/telegram`
- `POST /auth/dev` (только Development)
- `GET /wisdoms/random`

### 4.2 User endpoints

- `GET /events`
- `POST /events`
- `PATCH /events/{id}`
- `DELETE /events/{id}`
- `GET /summaries/{periodType}` (`periodType`: `weekly|monthly|yearly`)
- `PUT /summaries/{periodType}/highlight` (manual highlight flow)
- `GET /day-rating`
- `PUT /day-rating`
- `GET /settings`
- `PUT /settings`

### 4.3 Admin auth endpoints

- `POST /admin/auth/login`
- `GET /admin/auth/me`
- `POST /admin/auth/logout`

### 4.4 Admin product endpoints

- `GET /admin/users`
- `GET /admin/users/{id}`
- `GET /admin/metrics/dashboard`
- `GET /admin/events`
- `GET /admin/summaries`
- `GET /admin/feedback`
- `PATCH /admin/feedback/{id}/read`
- `POST /admin/feedback/{id}/reply`
- `GET /admin/delivery-attempts`
- `POST /admin/messaging/broadcast`
- `GET /admin/messaging/broadcasts`
- `GET /admin/messaging/broadcasts/{id}`
- `GET /admin/audit-logs`

## 5) Data Model (Current MVP)

Ниже перечислены актуальные сущности текущей схемы и доменной модели (без архивных/удалённых таблиц).

### 5.1 User domain

- `users` — профиль пользователя, статус, soft-delete markers.
- `user_settings` / `users_settings` — timezone, reminders, week_end и пользовательские флаги.
- `user_sessions` — user auth sessions.
- `auth_replay_cache` — защита от повторного использования auth init data.
- `timezone_history` — история смен timezone.
- `week_schedule_history` — история weekly schedule (`week_end`) и transition boundaries.
- `events` — события дня (`local_date`, `text`, `importance`, soft-delete).
- `day_ratings` — оценка дня.
- `summaries` — периодические записи (`weekly|monthly|yearly`) с `highlight_event_id`.
- `user_feedback` — сообщения обратной связи пользователя и статус обработки (`new/read/responded`).
- `wisdoms` — контент для `GET /wisdoms/random`.

### 5.2 Delivery / reliability / dedupe

- `delivery_attempts` — единый журнал попыток доставки Telegram-сообщений (reminders, admin replies, admin broadcasts и др.) со статусами/attempt counters.
- `operation_id_cache` — dedupe client operations (например highlight update) с TTL cleanup worker'ом.

### 5.3 Admin domain

- `admin_users` — учетные записи администраторов и роли.
- `admin_sessions` — admin sessions (cookie/bearer-backed).
- `audit_logs` — журнал admin-действий.
- `admin_broadcast_campaigns` — кампании массовых рассылок; фактический прогресс определяется по связанным `delivery_attempts`.

## 6) NFR (Current MVP)

## NFR-1. Надёжность и идемпотентность

- Server-side дедупликация клиентских операций для защищённых endpoints.
- Повторные Telegram delivery attempts ограничены по числу попыток.
- Retry flow различает retry ошибок и queued admin broadcast initial sends.
- Регистрация пользователя устойчива к гонкам (уникальность по Telegram ID).

## NFR-2. Безопасность

- Проверка Telegram webhook secret.
- Проверка Telegram init data и replay protection для user auth.
- Admin auth через HttpOnly-cookie и RBAC.
- Ограничение доступа к чувствительным данным по ролям (`admin/operator/analyst`).

## NFR-3. Наблюдаемость и эксплуатация

- Health endpoints: `/health`, `/health/db`.
- Delivery attempts, audit logs и campaign stats доступны в Admin UI/API.
- Отдельные cleanup workers поддерживают retention для audit logs и purge удалённых пользователей.

## NFR-4. Производительность и UX

- API должен отвечать быстро на действия пользователя; тяжёлые операции рассылки вынесены в async queue/campaign flow.
- Mini App должен корректно работать внутри Telegram WebView (safe areas, keyboard, theme changes).

## 7) Acceptance Criteria (Baseline)

1. Пользователь может авторизоваться в Mini App через `POST /auth/telegram` и получить доступ к `events`, `summaries`, `settings`, `day-rating`.
2. `POST /events` не создаёт второе событие за тот же `local_date` для одного пользователя (возвращает `409 event_exists`).
3. Пользователь может выбрать highlight для `weekly`, `monthly`, `yearly` через `PUT /summaries/{periodType}/highlight` с корректной валидацией периода/принадлежности события.
4. `Month` выбирает только weekly-highlight, `Year` — только monthly-highlight.
5. Напоминания и другие Telegram-доставки пишут `delivery_attempts`; retry выполняется `DeliveryRetryService`.
6. `POST /admin/messaging/broadcast` создаёт campaign + queue (`pending` attempts), а фактическая отправка выполняется асинхронно.
7. Admin UI/API показывает список кампаний и агрегированные delivery counters по кампании.
8. Admin audit логирует login/logout и операционные действия; `/admin/audit-logs` доступен только роли `admin`.
9. Admin feedback workflow работает: list / mark read / reply via Telegram (с audit и delivery tracking).
10. В API процессе зарегистрированы только актуальные HostedService из раздела FR-12; period job workers отсутствуют.

## 8) Связанные документы

- `README.md` — запуск, env, эксплуатационные заметки, актуальные ограничения/поведение.
- `docs/IMPLEMENTATION_STATUS.md` — проверенные последние коммиты и изменения реализации.
- `docs/METRICS.md` — формулы метрик, SQL-проверки, текущий статус prompt conversion.
- `docs/RUNTIME_WORKERS.md` — runtime-воркеры, интервалы, env, guarantees.
