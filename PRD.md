# PRD v1.3 — MVP
## Сервис фиксации главных событий (Telegram Bot + Telegram Mini App + Web Admin UI)

## 1) Цель MVP
Дать пользователю простой и регулярный процесс фиксации событий:
**день → неделя → месяц → год**,  
с понятными правилами периодов, без дублей и с прозрачной админской операционкой.

---

## 2) Scope MVP

### Входит
1. **Telegram Bot**
   - ежедневные напоминания,
   - ввод события дня,
   - запуск формирования итогов (week/month/year),
   - ручные команды/кнопки.

2. **Telegram Mini App (основной пользовательский UI)**
   - экраны: Сегодня, Неделя, Месяц, Год, Настройки.

3. **Web Admin UI**
   - Dashboard метрик,
   - Users,
   - Content,
   - Operations,
   - Security/Audit.

### Не входит (MVP out of scope)
- AI-саммаризация,
- командные аккаунты/шаринг,
- нативные мобильные приложения,
- расширенная BI-аналитика.

---

## 3) Functional Requirements (FR)

## FR-1. Событие дня
`Событие дня содержит: text (1..500), local_date (YYYY-MM-DD в timezone пользователя), importance (int 1..5).`

- Создание доступно через Bot и Mini App.
- Importance задаётся звёздами `★` (1..5).
- local_date по умолчанию = текущая локальная дата пользователя.
- Редактирование/удаление события разрешено пользователю в течение 7 суток; после этого только read-only.

---

## FR-2. Timezone как источник истины
`Все расчеты периодов (day/week/month/year), напоминания и границы дат выполняются строго в IANA timezone пользователя.`

- Timezone хранится в настройках пользователя.
- Все cron/job расчёты выполняются от него.
- При смене timezone новая TZ применяется только к новым периодам; уже закрытые периоды не пересчитываются.

---

## FR-3. Ежедневные напоминания
- Пользователь настраивает:
  - время вечернего напоминания,
  - включено/выключено,
  - timezone.
- Напоминание отправляется ежедневно в локальное время пользователя.

---

## FR-4. Weekly summary
`Weekly summary строится по интервалу [week_start, week_end], где week_end настраиваемый (default Sunday).`

- В день закрытия недели:
  `weekly-flow автозапускается только один раз после первого сохранения события дня.`
- Если в день закрытия недели пользователь не добавил ни одного события, автоформирование не запускается; период остаётся доступен для ручного запуска.
- Есть ручной запуск в любой момент.
- В weekly попадают только события целевой недели.

---

## FR-5. Manual run как upsert
`Ручной запуск period summary всегда доступен; при существующем summary выполняется update (upsert), без создания дубля.`

- Применимо к week/month/year.
- Детерминированное правило выбора периода при manual run без явного выбора периода в UI:
  - **week:** берётся последний полностью завершённый weekly-период относительно `now` в TZ пользователя;
  - **month:** берётся последний полностью завершённый календарный месяц;
  - **year:** берётся последний полностью завершённый календарный год.
- Если пользователь явно выбрал период в UI, backend обязан использовать именно его `period_start/period_end`.
- Повторный manual run того же периода в тот же день выполняет upsert в ту же запись summary (по уникальному ключу), без создания новой.

---

## FR-6. Monthly summary
`Monthly summary включает только события, чья local_date попадает в закрываемый календарный месяц пользователя.`

- Запрос на формирование — в последний календарный день месяца.
- Если в последний день месяца нет новых событий, пользователь получает мягкое напоминание о ручном запуске monthly summary.
- Доступен ручной запуск.

---

## FR-7. Yearly summary
`Yearly summary включает только события, чья local_date попадает в закрываемый календарный год пользователя.`

- Запрос на формирование — 31 декабря (локальная дата пользователя).
- Если 31 декабря нет новых событий, пользователь получает мягкое напоминание о ручном запуске yearly summary.
- Доступен ручной запуск.

---

## FR-8. Идемпотентность периодических задач
`Для period-job использовать idempotency_key = user_id + period_type + period_start + period_end; повторный запуск возвращает существующий результат.`

- Применяется для авто- и ручных запусков.
- Гарантирует отсутствие дублей summary.
- Ключ хранится в БД минимум 180 дней.

---

## FR-9. Telegram Bot UX
- Inline-кнопки выбора важности `1..5 звезд`.
- Быстрые сценарии:
  - добавить событие дня,
  - сформировать week/month/year вручную,
  - открыть Mini App,
  - открыть настройки.
- Anti-double-submit: повторные клики по одной action-кнопке в 3 секунды не создают дубли.

---

## FR-10. Telegram Mini App (обязательные экраны)
1. **Сегодня** — добавление событий дня, важность звёздами.  
2. **Неделя** — просмотр и формирование weekly summary.  
3. **Месяц** — просмотр и формирование monthly summary.  
4. **Год** — просмотр и формирование yearly summary.  
5. **Настройки** — timezone, reminder time, reminder on/off, week_end.

---

## FR-11. Admin UI и метрики
`Admin UI: RBAC (admin/operator/analyst) + audit log всех действий (retention 180 дней); DAU/WAU/MAU и conversion фиксируются в отдельном Metrics Spec с формулами.`

Минимальные разделы:
- **Dashboard** (DAU/WAU/MAU, события, conversion),
- **Users** (профиль настроек, последняя активность),
- **Content** (события + summaries),
- **Operations** (доставки, ошибки, ретраи),
- **Audit** (лог админ-действий).

---

## FR-11.1 Prompt delivery tracking (для метрик)
- Каждая отправка prompt на формирование week/month/year фиксируется в `prompt_deliveries`.
- Обязательные поля: `prompt_id`, `user_id`, `period_type`, `period_start`, `period_end`, `sent_at`, `channel`, `status`.
- `prompt_deliveries` является единственным источником `sent_prompts` и `prompt_sent_at` для метрики Prompt→Summary conversion.

---

## FR-12. User Auth (Bot + Mini App API)
- Для Mini App обязательна валидация Telegram init data (подпись/время жизни).
- Сессия пользователя: TTL 24 часа, renew on activity.
- Replay protection (обязательно):
  - storage key: `sha256(init_data_raw)`;
  - TTL ключа: 10 минут;
  - первый валидный запрос с данным ключом помечает ключ как used;
  - повторный запрос с тем же ключом после успешной фиксации used → отклоняется (`409 replay_detected`);
  - для параллельных запросов из одного открытия Mini App допускается только один winner (atomic set-if-not-exists), остальные получают `409 replay_detected`.

---

## FR-13. API Contract (минимум)
Обязательный список API для MVP:
- `POST /events` (create event)
- `PATCH /events/{id}` (edit event)
- `DELETE /events/{id}` (soft delete)
- `POST /summaries/{periodType}/run` (manual run)
- `GET /summaries/{periodType}` (list/get)
- `GET/PUT /settings`
- `GET /metrics/dashboard` (admin)

Для каждого endpoint: request schema, response schema, error codes, idempotency behavior.

## FR-14. Summary contract (обязательно)
`summary` хранит строго определённый контракт:
- `id`, `user_id`, `period_type`, `period_start`, `period_end`, `status`, `version`, `last_generated_at`;
- `content` (структурированный JSON с выбранными событиями и их order);
- `source_event_ids` (множество event id, использованных при генерации).

Правила пересборки (upsert):
- повторная генерация того же периода увеличивает `version` на 1;
- `last_generated_at` обновляется;
- `source_event_ids` и `content` пересчитываются из актуальных данных периода.
- если событие удалено/изменено после генерации, эффект применяется только после следующего manual/auto run этого периода.

---

## 4) Data Model & State Model (обязательно для MVP)

## 4.1 Минимальные сущности
- `users_settings`
- `events`
- `summaries`
- `period_jobs`
- `delivery_attempts`
- `prompt_deliveries` (источник `sent_prompts` / `prompt_sent_at` для prompt→summary метрики)
- `audit_logs`

## 4.2 Ключевые уникальные ограничения
- `summaries`: unique(`user_id`, `period_type`, `period_start`, `period_end`)
- `period_jobs`: unique(`idempotency_key`)
- `events`: unique(`user_id`, `external_source`, `external_event_id`) для дедупа внешних апдейтов
- `prompt_deliveries`: unique(`prompt_id`) и unique(`user_id`, `period_type`, `period_start`, `period_end`, `sent_at`)

## 4.3 Жизненный цикл period_jobs/summaries
Статусы job: `pending -> running -> success | failed | retried`.
- `failed` при временной ошибке может перейти в `retried` и снова в `running`.
- `success` терминальный.
- Для partial success обязателен recovery: если summary сохранён, повторный run возвращает уже сохранённый результат.

## 4.4 Concurrency model (one-run guarantee)
- Триггер weekly-flow «после первого сохранения события дня» реализуется через атомарную транзакцию:
  1) insert/update события;
  2) попытка `INSERT` в `period_jobs` с уникальным `idempotency_key`;
  3) при конфликте unique запуск не создаётся (already exists).
- Для ручных/авто запусков периодов winner определяется только БД (unique + transaction), не клиентом.
- Параллельные воркеры берут job через lock (например, `FOR UPDATE SKIP LOCKED` или эквивалент очереди).
- Повторные доставки webhook/клики не создают новый run при существующем `idempotency_key`.

---

## 5) NFR (Non-Functional Requirements)

## NFR-1. Delivery + retries
`Daily reminder отправляется в заданное локальное время (допуск ±5 минут); при transient-ошибках до 5 ретраев с exponential backoff.`

- Транзиентные ошибки (сетевые, 429, 5xx) — retry.
- Непоправимые ошибки — terminal status + лог.
- DST policy (обязательно):
  - **spring-forward (локальное время не существует):** отправка сдвигается на ближайшее следующее валидное локальное время;
  - **fall-back (локальное время встречается дважды):** отправка выполняется только в первое наступление времени;
  - scheduler хранит и логирует фактический UTC timestamp отправки для аудита SLA.

## NFR-2. Надёжность и консистентность
- Все period-jobs идемпотентны.
- Дубли webhook/job не приводят к дублям данных.
- DB-операции summary — атомарны.

## NFR-3. Наблюдаемость
- Логируются:
  - отправки напоминаний,
  - запуск/результат period-jobs,
  - ошибки и причины.
- У каждой job есть correlation/job id.

## NFR-4. Безопасность
- RBAC для Admin UI (`admin/operator/analyst`).
- Аудит админ-действий (retention 180 дней).
- Секреты и токены хранятся вне кода (env/secret storage).
- Доступ к PII — только по роли согласно матрице:
  - `admin`: полный доступ,
  - `operator`: read-only к текстам событий, без удаления,
  - `analyst`: только агрегированные метрики, без текстов событий.

## NFR-5. Производительность (MVP)
- API p95 (типовые операции чтения/создания) ≤ 1.5 сек.
- Задержка генерации summary после триггера ≤ 2 минут p95.
- Ключевые UI-операции не требуют ручного refresh.

## NFR-6. Эксплуатационные лимиты MVP
- Плановая нагрузка: до 10k MAU.
- До 100k событий/сутки.
- До 20 rps пиковой нагрузки API.

## NFR-7. Data retention
- `events` и `summaries` хранятся бессрочно (для MVP), пока пользователь не запросит удаление.
- Удаление пользователя: soft-delete сразу, hard-delete персональных данных в течение 30 дней.

---

## 6) Metrics Spec (обязательное приложение к MVP)
До старта разработки должен быть зафиксирован отдельный `METRICS.md` с формулами:
- DAU/WAU/MAU,
- conversion reminder→event,
- conversion prompt→summary,
- источники истины (таблицы/события),
- правила дедупликации событий аналитики.

Без `METRICS.md` AC по метрикам считаются непроверяемыми.

---

## 7) Acceptance Criteria (baseline for MVP)

1. **Daily event validation**  
   Событие не сохраняется, если `text` пустой/длина >500, importance вне 1..5, local_date некорректна.

2. **Timezone correctness**  
   Все границы day/week/month/year считаются в IANA timezone пользователя, включая переходы DST.

3. **Weekly auto-trigger once**  
   В день week_end после первого сохранения события дня weekly-flow запускается ровно один раз.

4. **Manual upsert behavior**  
   Ручной запуск week/month/year при уже существующем summary обновляет его (upsert), не создаёт дубль.

5. **Monthly scope correctness**  
   В monthly summary включаются только события закрываемого календарного месяца пользователя.

6. **Yearly scope correctness**  
   В yearly summary включаются только события закрываемого календарного года пользователя.

7. **Idempotency guarantee**  
   Повторный запуск period-job с одинаковым `idempotency_key` возвращает существующий результат без дублей.

8. **Reminder SLA**  
   Не менее 99% reminder-доставок — в окно ±5 минут (за исключением внешних outage Telegram/сети).

9. **Retry policy**  
   Transient-ошибка вызывает до 5 retry с exponential backoff; при исчерпании — terminal status и запись в логи.

10. **Admin access control**  
   Пользователь без роли RBAC не получает доступ к Admin UI/API.

11. **Audit completeness**  
   Все изменения через Admin UI фиксируются в audit log (actor, action, target, timestamp, outcome).

12. **Metrics consistency**  
   DAU/WAU/MAU и conversion в Dashboard совпадают с формулами из `METRICS.md` на тестовом датасете.

13. **Core UX completeness**  
   В Mini App доступны и рабочие 5 экранов: Сегодня/Неделя/Месяц/Год/Настройки.

14. **No duplicate summaries**  
   При ретраях, двойных кликах, повторных webhook/event не возникает дублирующих summary записей.

15. **Data constraints enforced**  
   Уникальные ограничения `summaries` и `period_jobs` реально работают на уровне БД.

16. **Job lifecycle correctness**  
   Для каждого запуска period-job корректно выставляются статусы state machine (pending/running/success/failed/retried).

17. **Auth security for user API**  
   Mini App API отклоняет невалидный/просроченный/replayed Telegram init data.

18. **Edge-cases covered**  
   Пройдены тесты: смена timezone, DST-переход, ручной запуск задним числом, `week_end` ≠ Sunday.

19. **Deterministic manual-period selection**  
   При manual run без выбора периода backend всегда выбирает один и тот же «последний полностью завершённый» период по правилам FR-5.

20. **Replay protection correctness**  
   Повторный запрос с тем же `init_data_raw` в течение TTL отклоняется с `409 replay_detected`; в параллельных запросах только один проходит.

21. **DST scheduler behavior**  
   Для spring-forward/fall-back поведение reminder соответствует политике NFR-1 и подтверждено тестами.

22. **Summary contract compliance**  
   Каждая запись `summaries` содержит обязательные поля контракта FR-14; при повторной генерации корректно меняются `version` и `last_generated_at`.

23. **One-run concurrency guarantee**  
   При конкурентных запросах/ретраях создаётся не более одного period-job на `idempotency_key`; конкурентные воркеры не создают дублей summary.

24. **Prompt metrics source defined**  
   Метрика Prompt→Summary conversion рассчитывается только из `prompt_deliveries` (`sent_prompts`, `prompt_sent_at`) и `summaries`.

---

## 8) Baseline for development
Данный **PRD v1.3** считается базовой спецификацией MVP и передаётся в разработку. Приложение `METRICS.md` зафиксировано.
