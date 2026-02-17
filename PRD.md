# PRD v2.13 — MVP
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

## FR-0. User Registration & Onboarding
`При первом взаимодействии (команда /start в Bot или первое открытие Mini App) создаётся запись пользователя с настройками по умолчанию.`

- **Точки входа:** `/start` в Telegram Bot, первое открытие Mini App (по Telegram init data).
- **Обязательные настройки с дефолтами:**
  - `timezone`: определяется автоматически при первом открытии Mini App через `Intl.DateTimeFormat().resolvedOptions().timeZone` (JavaScript). Если пользователь начал через Bot (без Mini App), timezone = `UTC` и бот отправляет one-time подсказку: «Откройте Mini App или настройки, чтобы мы определили ваш часовой пояс». Пользователь может изменить timezone в любой момент в Настройках.
  - `reminder_time`: `21:00` (формат `HH:mm`, 24-часовой, без секунд, локальное время пользователя).
  - `reminder_enabled`: `true`.
  - `week_end`: `Sunday`.
- **Если timezone = UTC (fallback)**, при первом создании события пользователь получает one-time подсказку настроить timezone в Настройках.
- При регистрации также создаются:
  - начальная запись в `week_schedule_history` (FR-4.4) с `effective_from_local_date` = `дата_регистрации - 31 день` (на 1 день больше backdate-окна FR-1, чтобы покрыть edge case сдвига `today_local` при смене TZ с UTC на TZ с отрицательным offset);
  - начальная запись в `timezone_history` (FR-2.2) с текущей timezone.
- Повторный `/start` или повторное открытие Mini App не создают дубль пользователя (idempotent upsert по `telegram_user_id`).

---

## FR-1. Событие дня
`Событие дня содержит: text (1..500), local_date (YYYY-MM-DD в timezone пользователя), importance (int 1..5).`

- Создание доступно через Bot и Mini App.
- Importance задаётся звёздами `★` (1..5).
- `local_date` по умолчанию = текущая локальная дата пользователя. Допустимый диапазон: от `текущая_дата - 30 дней` до `текущая_дата` включительно (события в будущем запрещены; бэкдейтинг ограничен 30 днями).
- Редактирование/удаление события разрешено пользователю в течение 7 суток (168 часов) от `created_at` (момент создания); после этого только read-only. Soft-delete: поле `deleted_at` ≠ NULL; удалённые события не отображаются в UI и исключаются из генерации summary при следующем run.
- **Редактируемые поля:** `text`, `importance`. **Нередактируемые поля:** `local_date` (дата фиксируется при создании и не может быть изменена, чтобы исключить перемещение события между периодами и нарушение целостности уже сгенерированных summary).

---

## FR-2. Timezone как источник истины
`Все расчеты периодов (day/week/month/year), напоминания и границы дат выполняются строго в IANA timezone пользователя.`

- Timezone хранится в настройках пользователя.
- Все cron/job расчёты выполняются от него.
- При смене timezone новая TZ применяется только к новым периодам; уже закрытые периоды не пересчитываются.

### FR-2.1 Инклюзивность границ периодов
- `local_date` имеет тип `DATE` (без времени), поэтому границы периодов всегда однозначны.
- Все периоды используют **инклюзивные границы**: `local_date >= period_start AND local_date <= period_end`.
- **Неделя:** `[week_start, week_end]` — 7 дней включительно (FR-4.1).
- **Месяц:** `[первый день месяца, последний день месяца]` в TZ пользователя.
- **Год:** `[1 января, 31 декабря]` в TZ пользователя.

### FR-2.2 Смена timezone
При смене timezone пользователем применяются следующие правила:

**Момент вступления в силу:** немедленно. Новая TZ используется для всех последующих вычислений `today_local`, backdate-window, авто-триггеров и напоминаний.

**Влияние на периоды:**
- **Закрытые периоды** (summary в статусе `generated` или `failed`): не пересчитываются. `period_start` и `period_end` — DATE, они не зависят от TZ.
- **Открытый weekly-период** (текущий, без summary): границы пересчитываются по `week_schedule_history` и новой TZ. Если `today_local` в новой TZ сдвинулся так, что текущий `week_end` уже в прошлом — период считается завершённым и доступен для manual run, но авто-триггер **не** срабатывает ретроактивно.
- **Monthly/Yearly:** границы календарных месяцев/лет определяются по новой TZ; незакрытые периоды пересчитываются автоматически.

**Влияние на reminder schedule:**
- Reminder пересчитывается немедленно: `reminder_time` интерпретируется в новой TZ.
- Если ближайшее запланированное напоминание в новой TZ уже в прошлом — пропускается (не отправляется ретроактивно).

**Backdate window:**
- `текущая_дата` пересчитывается в новой TZ. Допустимый диапазон `local_date` для создания/редактирования событий определяется от новой `текущая_дата`.

**История:**
- Смена timezone фиксируется в `timezone_history` (таблица: `id`, `user_id` FK → users, `timezone` text NOT NULL, `effective_from` timestamptz NOT NULL, `created_at` timestamptz). Первая запись создаётся при регистрации (FR-0).
- История используется для аудита, но **не** для ретроактивного пересчёта периодов — все `local_date` и `period_start/period_end` хранятся как DATE и не зависят от текущей TZ.

**Edge cases:**
- Смена TZ в день `week_end`: если в старой TZ сегодня `week_end`, но в новой TZ сегодня ещё `week_end - 1` → авто-триггер не сработает в этот день (day boundary сдвинулся).
- Смена TZ в последний день месяца/года: аналогично — авто-триггер привязан к `today_local` в актуальной TZ.

**Ограничение на частоту:** не более одной смены timezone в 24 часа. При попытке → `429 timezone_change_cooldown` с сообщением «Повторная смена timezone возможна через N часов».

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

### FR-4.1 Вычисление границ недели
- `week_end` — настраиваемый день недели (по умолчанию Sunday).
- `week_start` = день, следующий за `week_end` предыдущей недели (т.е. `week_end + 1` предыдущего цикла). Если `week_end = Sunday`, то `week_start = Monday`.
- Недельный интервал: все события с `local_date` от `week_start` до `week_end` включительно (`local_date >= week_start AND local_date <= week_end`). Поскольку `local_date` имеет тип `DATE` (без времени), границы однозначны.

### FR-4.2 Авто-триггер
- В день закрытия недели (`week_end`):
  `weekly-flow автозапускается только один раз после создания (POST) события дня, чья local_date попадает в целевой период.`
- **Условие запуска (оба должны быть истинны):**
  1. Сохранённое событие имеет `local_date ∈ [period_start, period_end]` (событие с backdated `local_date` вне целевого периода **не** вызывает авто-триггер, даже если сохранено в день закрытия);
  2. В целевом периоде есть хотя бы одно не-удалённое событие (проверка в той же транзакции, шаг 3 в 4.4).
- Триггер вызывается только при **создании** (`POST /events`), не при редактировании (`PATCH`) или удалении (`DELETE`).
- Если в день закрытия недели ни одно новое событие не попало в целевой период, автоформирование не запускается; период остаётся доступен для ручного запуска.
- Записывается `prompt_delivery` с `channel=auto` (аналогично FR-6/FR-7).
- Есть ручной запуск в любой момент.
- В weekly попадают только события целевой недели.

### FR-4.3 Смена `week_end`

При смене `week_end` создаётся одноразовый **transition period** переменной длины. Это единственное исключение из правила «неделя = 7 дней».

**Определения:**
- `change_date` — локальная дата пользователя в момент смены настройки.
- «Последняя старая неделя» — последний weekly-период по старому расписанию, чей `period_end` ≤ `change_date`.
- `last_old_end` — `period_end` последней старой недели.

**Алгоритм `compute_transition(old_week_end, new_week_end, change_date)`:**
1. `transition_start` = `last_old_end + 1 день`.
2. Найти ближайший `new_week_end` ≥ `transition_start`: `transition_end` = первый день ≥ `transition_start`, чей день недели = `new_week_end`.
3. Если `transition_end == transition_start` и длина transition = 1 день — допустимо (минимум 1 день).
4. `first_new_week_start` = `transition_end + 1 день`. Все последующие weekly — строго 7 дней.

**Длина transition period:** от 1 до 7 дней (максимум: `transition_start` — следующий день после `new_week_end` → 7 дней до следующего `new_week_end` включительно).

**Примеры:**
- **Sun→Wed, change_date = Thu 2025-01-16.** last_old_end = Sun 2025-01-12. transition: Mon 13 → Wed 15 (3 дня). Первая новая неделя: Thu 16 → Wed 22.
- **Wed→Sun, change_date = Fri 2025-01-17.** last_old_end = Wed 2025-01-15. transition: Thu 16 → Sun 19 (4 дня). Первая новая неделя: Mon 20 → Sun 26.
- **Sun→Sun (нет изменения).** Transition не создаётся. Смена отклоняется как no-op.
- **Смена в день week_end: Sun→Wed, change_date = Sun 2025-01-19.** last_old_end = Sun 2025-01-19 (текущая неделя завершается сегодня). transition: Mon 20 → Wed 22 (3 дня). Первая новая: Thu 23 → Wed 29.

**Правила:**
- Наложение периодов запрещено: transition начинается строго после `last_old_end`.
- **Авто-триггер для transition period:** применяются те же правила FR-4.2: после `POST /events` с `local_date ∈ [transition_start, transition_end]` в день `transition_end`, при наличии событий в периоде. Целевой период определяется по `week_schedule_history`.
- **Catch-up:** если `transition_end` < `change_date` (transition period уже в прошлом на момент смены), авто-триггер невозможен. В этом случае при смене `week_end` backend автоматически создаёт period_job для transition period (если в периоде есть события). Если событий нет — transition считается пустым и автоматически завершённым (не блокирует повторную смену `week_end`).
- Transition period хранится как summary с `period_type = weekly` и нестандартной длиной; FR-5 (manual run без выбора) учитывает его наравне с обычными weekly-периодами.
- **Ограничение на частоту смены:** повторная смена `week_end` запрещена, пока transition period предыдущей смены не завершён (summary в статусе `generated` или период пуст). При попытке → `409 transition_pending` с сообщением «Дождитесь завершения переходного периода».
- Смена `week_end` фиксируется в `week_schedule_history` (FR-4.4) для воспроизводимости расчётов.

### FR-4.4 История смены `week_end`
- Таблица `week_schedule_history`: `id`, `user_id` FK → users, `week_end` (enum Monday..Sunday), `effective_from_local_date` DATE, `transition_start` DATE NULL, `transition_end` DATE NULL, `created_at` timestamptz.
- **Transition boundaries:** при смене `week_end` (FR-4.3) запись содержит `transition_start` и `transition_end` — явные границы переходного периода. При начальной регистрации (FR-0) оба поля = NULL (transition отсутствует). Это обеспечивает детерминированную реконструкцию любого периода без вычислений по соседним записям.
- При каждой смене `week_end` в настройках создаётся новая запись с `effective_from_local_date` = дата начала первого нового цикла (после transition period).
- Начальная запись создаётся при регистрации пользователя (FR-0) с `effective_from_local_date` = `дата_регистрации - 31 день` (на 1 день больше backdate-окна FR-1, покрывает edge case сдвига `today_local` при смене TZ).
- **Fallback-правило:** если `local_date < min(effective_from_local_date)` для данного пользователя, используется earliest запись `week_schedule_history` (самая старая). Это гарантирует, что для любого допустимого `local_date` всегда существует расписание, даже при непредвиденных сдвигах дат.
- Для вычисления границ любого weekly-периода backend использует историю, а не только текущее значение `week_end` из `users_settings`.

---

## FR-5. Manual run как upsert
`Ручной запуск period summary всегда доступен; при существующем summary выполняется update (upsert), без создания дубля.`

- Применимо к week/month/year.
- Детерминированное правило выбора периода при manual run без явного выбора периода в UI:
  - Период считается **полностью завершённым**, если `today_local > period_end` (строго больше; в день `period_end` период ещё не завершён).
  - **Исключение:** если `today_local == period_end` и авто-триггер уже создал summary (status ∈ {`generating`, `generated`}) — период считается завершённым для целей manual run (пользователь может пересобрать).
  - **week:** берётся последний полностью завершённый weekly-период (включая transition periods) относительно `now` в TZ пользователя;
  - **month:** берётся последний полностью завершённый календарный месяц;
  - **year:** берётся последний полностью завершённый календарный год.
- Если пользователь явно выбрал период в UI, backend обязан использовать именно его `period_start/period_end`.
- Повторный manual run того же периода в тот же день выполняет upsert в ту же запись summary (по уникальному ключу), без создания новой.

---

## FR-6. Monthly summary
`Monthly summary включает только события, чья local_date попадает в закрываемый календарный месяц пользователя.`

- **Авто-триггер:** в последний календарный день месяца (локальная дата пользователя), после создания (`POST`) события дня с `local_date` в целевом месяце, monthly-flow автозапускается один раз. Применяются те же правила бэкдейтинга и транзакционной проверки наличия событий, что в FR-4.2 и шаге 3 транзакции 4.4. Записывается `prompt_delivery` с `channel=auto`.
- Если в последний день месяца пользователь не добавил ни одного события, авто-формирование не запускается; пользователь получает мягкое напоминание о ручном запуске monthly summary.
- Доступен ручной запуск в любой момент.

---

## FR-7. Yearly summary
`Yearly summary включает только события, чья local_date попадает в закрываемый календарный год пользователя.`

- **Авто-триггер:** 31 декабря (локальная дата пользователя), после создания (`POST`) события дня с `local_date` в целевом году, yearly-flow автозапускается один раз. Применяются те же правила бэкдейтинга и транзакционной проверки наличия событий, что в FR-4.2 и шаге 3 транзакции 4.4. Записывается `prompt_delivery` с `channel=auto`.
- Если 31 декабря пользователь не добавил ни одного события, авто-формирование не запускается; пользователь получает мягкое напоминание о ручном запуске yearly summary.
- Доступен ручной запуск в любой момент.

---

## FR-8. Идемпотентность и повторная генерация периодических задач

### FR-8.1 Idempotency key и run_number
`Для period-job используется idempotency_key = user_id + period_type + period_start + period_end + run_number; параллельный запуск с тем же ключом не создаёт дубль job.`

- **`run_number`** хранится в таблице `period_run_counters` (unique по `user_id`, `period_type`, `period_start`, `period_end`; поле `last_run_number`).
- **Первичная инициализация:** при первом обращении к периоду — `INSERT INTO period_run_counters ... VALUES (..., 1) ON CONFLICT DO NOTHING`; `last_run_number` стартует с 1.
- **Способ получения `run_number` зависит от режима запуска** (FR-8.2):
  - *Idempotent trigger (FR-8.2a):* `SELECT last_run_number` — без инкремента.
  - *Force re-run (FR-8.2b):* `UPDATE ... SET last_run_number = last_run_number + 1 RETURNING last_run_number` — атомарный инкремент.
- Ключ хранится в БД минимум 180 дней.

### FR-8.2 Два режима запуска
Period-job запускается в одном из двух режимов:

**a) Idempotent trigger** (авто-триггер + anti-double-submit):
- Используется авто-триггерами (FR-4, FR-6, FR-7) и как защита от double-click (FR-9).
- **Auto-trigger (server-initiated):** `run_number` = текущее значение `last_run_number` из `period_run_counters` (SELECT без инкремента). Если строка не существует, используется `run_number = 1` (с upsert строки `last_run_number = 1`). Все конкурентные авто-триггеры получают одинаковый `idempotency_key` → unique constraint в `period_jobs` выбирает winner, остальные получают conflict.
  - **Terminal fail recovery:** если существующий `period_job` с текущим `idempotency_key` находится в терминальном статусе `failed` (attempt_count ≥ 3), авто-триггер **атомарно инкрементирует** `last_run_number` (`UPDATE ... SET last_run_number = last_run_number + 1 RETURNING last_run_number`) и создаёт новый job с новым `idempotency_key`. Это предотвращает «залипание» периода без summary после terminal failure.
- **UI/Bot double-click protection:** клиент передаёт `client_operation_id` (UUID, генерируется на стороне клиента/бота при каждом осознанном действии пользователя). Backend проверяет `client_operation_id` в dedupe-кэше (TTL 5 минут). Повторный запрос с тем же `client_operation_id` → возвращает результат существующего job без создания нового.
- Авто-триггер при существующем summary в статусе `generated` **не создаёт** новый job (период уже обработан).

**b) Force re-run** (manual run, FR-5):
- Пользователь явно запрашивает пересборку summary.
- Backend атомарно выделяет новый `run_number`: `INSERT INTO period_run_counters ... ON CONFLICT DO UPDATE SET last_run_number = last_run_number + 1 RETURNING last_run_number`. **Только force re-run инкрементирует счётчик.**
- Создаётся новый period_job с новым `idempotency_key`.
- Summary пересчитывается по правилам FR-14 (version++, content/source_event_ids обновляются).

### FR-8.3 Partial success recovery
- Если job завершился с ошибкой после сохранения summary в статусе `generated` (partial success): повторный retry обнаруживает `generated` summary и завершается как `success` без пересчёта.
- Гарантия отсутствия дублей summary обеспечивается unique constraint на `summaries` (раздел 4.2).

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

### FR-12.1 Mini App auth handshake
- Mini App при открытии вызывает `POST /auth/telegram` с Telegram init data.
- Backend валидирует подпись и время жизни init data. **Max age для `auth_date`:** init data с `auth_date` старше 300 секунд (5 минут) от текущего server time отклоняется → `401 init_data_expired`. Это ограничение действует **до** replay protection check и предотвращает использование перехваченного init data после истечения replay TTL.
- **Replay protection** применяется **только** к `POST /auth/telegram`:
  - storage key: `sha256(canonicalize(init_data_raw))`, где `canonicalize` — парсинг query-параметров init_data, сортировка по ключу (alphabetical), сериализация обратно в строку. Это предотвращает обход replay protection через перестановку параметров при валидной подписи;
  - TTL ключа: 300 секунд (5 минут) — совпадает с max age для `auth_date`, чтобы исключить окно, в котором replay cache ещё жив, а `auth_date` уже просрочен;
  - первый валидный запрос помечает ключ как used и сохраняет результат (session token) вместе с ключом;
  - повторный запрос с тем же ключом в пределах TTL → **идемпотентный ответ**: возвращается тот же session token (не `409`). Это предотвращает потерю авторизации при сетевых сбоях;
  - после TTL (5 мин) ключ удаляется; повторный запрос с просроченным `auth_date` → `401 init_data_expired`;
  - параллельные запросы: только один winner (atomic set-if-not-exists), проигравшие получают результат winner'а.
- При успешной валидации выдаётся session token (JWT или opaque).
- Сессия: TTL 24 часа, renew on activity (любой успешный API-вызов с валидным токеном).

### FR-12.2 User API auth
- Все user API endpoints (FR-13) требуют валидный session token в заголовке `Authorization`.
- Запросы без токена или с невалидным/просроченным токеном → `401 unauthorized`.

### FR-12.3 Bot auth
- Bot-взаимодействия аутентифицируются через `telegram_user_id` из webhook payload.
- Webhook от Telegram верифицируется через `X-Telegram-Bot-Api-Secret-Token` (настраивается при setWebhook).

---

## FR-12.4 Admin UI auth
- Admin UI использует отдельный auth-flow: `POST /admin/auth/login` (email + password или OAuth — определяется при реализации).
- При успехе выдаётся admin session token с ролью (`admin`/`operator`/`analyst`) в payload.
- Сессия admin: TTL 8 часов, без auto-renew (re-login по истечении).
- Все admin API endpoints проверяют роль из токена; несоответствие → `403 forbidden`.
- Login/logout события записываются в `audit_logs`.
- Роли назначаются через БД или admin seed; self-registration в MVP отсутствует.

---

## FR-13. API Contract (минимум)
Обязательный список API для MVP:
- `POST /auth/telegram` (Mini App auth handshake, FR-12.1)
- `POST /admin/auth/login` (admin login, FR-12.4)
- `GET /events` (список событий; query params: `from`, `to` — фильтрация по `local_date`, `limit`, `cursor` для пагинации; default: текущий день)
- `POST /events` (create event)
- `PATCH /events/{id}` (edit event)
- `DELETE /events/{id}` (soft delete)
- `POST /summaries/{periodType}/run` (manual run)
- `GET /summaries/{periodType}` (list/get; query params: `from`, `to`, `limit`, `cursor`)
- `GET/PUT /settings`
- `GET /metrics/dashboard` (admin)

Для каждого endpoint: request schema, response schema, error codes, idempotency behavior.

### FR-13.1 Минимальные API-схемы ключевых endpoints

**`POST /summaries/{periodType}/run`** (manual run)
```
Request:
  Path: periodType ∈ {weekly, monthly, yearly}
  Headers: Authorization: Bearer <session_token>
           X-Client-Operation-Id: <UUID> (обязательный, dedupe FR-9)
  Body (optional):
    {
      "period_start": "YYYY-MM-DD",  // если явный выбор периода в UI
      "period_end": "YYYY-MM-DD"     // если явный выбор периода в UI
    }
    Если body пустой/отсутствует — backend выбирает период по правилам FR-5.

Response 200 (job создан или уже существует):
    {
      "job_id": "uuid",
      "period_type": "weekly",
      "period_start": "2025-01-13",
      "period_end": "2025-01-19",
      "run_number": 1,
      "status": "pending" | "running" | "success" | "generated",
      "summary_id": "uuid" | null
    }

Errors:
    400 empty_period — нет событий в периоде (FR-14.3)
    400 invalid_period — некорректные даты
    401 unauthorized — невалидный/просроченный токен
    409 transition_pending — незавершённый transition period (FR-4.3)
    200 idempotent — client_operation_id dedupe hit: возвращает результат существующего job (тело идентично первому успешному ответу). HTTP 200, а не 429, чтобы клиент не уходил в retry-loop.
```

**`POST /events`** (create event)
```
Request:
  Headers: Authorization: Bearer <session_token>
           X-Client-Operation-Id: <UUID>
  Body:
    {
      "text": "string (1..500, required)",
      "importance": 1..5 (int, required),
      "local_date": "YYYY-MM-DD" (optional, default = today_local)
    }

Response 201:
    {
      "id": "uuid",
      "text": "...",
      "importance": 3,
      "local_date": "2025-01-15",
      "created_at": "ISO-8601"
    }

Errors:
    400 validation_error — text/importance/local_date не прошли валидацию
    400 date_out_of_range — local_date вне допустимого диапазона (FR-1)
    401 unauthorized
    200 idempotent — dedupe по client_operation_id: возвращает ранее созданный event (тело идентично первому ответу)
```

**`GET /summaries/{periodType}`** (list summaries)
```
Request:
  Headers: Authorization: Bearer <session_token>
  Query params:
    from: YYYY-MM-DD (фильтр по period_start >=)
    to: YYYY-MM-DD (фильтр по period_end <=)
    limit: int (1..100, default 20)
    cursor: string (opaque, base64-encoded; sort order: period_start DESC, period_end DESC; cursor кодирует last seen period_start + period_end для стабильной пагинации)

Response 200:
    {
      "items": [{ summary object per FR-14 }],
      "next_cursor": "string" | null
    }
```

## FR-14. Summary contract (обязательно)
`summary` хранит строго определённый контракт:
- `id`, `user_id`, `period_type`, `period_start`, `period_end`, `status`, `version`, `last_generated_at`;
- `content` (структурированный JSON, см. FR-14.1);
- `source_event_ids` (множество event id, использованных при генерации).

### FR-14.1 Алгоритм генерации summary (content)
Summary — это **агрегированный список событий периода** (AI-саммаризация вне scope MVP).

Алгоритм:
1. Выбрать все не-удалённые события пользователя, чья `local_date` попадает в `[period_start, period_end]`.
2. Отсортировать: по `local_date` ASC, затем по `importance` DESC, затем по `created_at` ASC.
3. Сформировать `content` JSON:
```json
{
  "events": [
    {
      "event_id": 123,
      "text": "Запустил MVP",
      "importance": 5,
      "local_date": "2025-01-15"
    }
  ],
  "total_events": 12,
  "period_start": "2025-01-13",
  "period_end": "2025-01-19"
}
```
4. `source_event_ids` = массив `event_id` всех включённых событий.

Пользователь в Mini App видит: список событий периода, сгруппированных по дням, с отметками importance (звёзды). Экраны Неделя/Месяц/Год отображают `content.events`, сгруппированные по `local_date`.

### FR-14.2 Правила пересборки (upsert)
- Повторная генерация того же периода увеличивает `version` на 1;
- `last_generated_at` обновляется;
- `source_event_ids` и `content` пересчитываются из актуальных данных периода.
- Если событие удалено/изменено после генерации, эффект применяется только после следующего manual/auto run этого периода.

### FR-14.3 Manual run при пустом периоде
- Если в периоде 0 не-удалённых событий **и summary для этого периода не существует**, manual run возвращает ошибку `400 empty_period` с сообщением «В выбранном периоде нет событий». Summary не создаётся.
- Если в периоде 0 не-удалённых событий, **но summary уже существует** (например, все события были удалены после генерации): force re-run выполняется, summary обновляется с `content = {"events": [], "total_events": 0, ...}`, `source_event_ids = []`, `version++`. Это гарантирует, что устаревший summary не остаётся навсегда.
- Авто-триггер при 0 событий не запускается (FR-4/FR-6/FR-7).

---

## 4) Data Model & State Model (обязательно для MVP)

## 4.1 Минимальные сущности
- `users` (профиль: `id`, `telegram_user_id`, `created_at`, `status`)
- `users_settings` (настройки: `user_id` FK → users, timezone, reminder_time, reminder_enabled, week_end)
- `period_run_counters` (атомарный счётчик run_number по периоду, FR-8.1)
- `week_schedule_history` (`id`, `user_id` FK → users, `week_end` enum(Monday..Sunday), `effective_from_local_date` DATE, `transition_start` DATE NULL, `transition_end` DATE NULL, `created_at` timestamptz; FR-4.4)
- `timezone_history` (`id`, `user_id` FK → users, `timezone` text NOT NULL, `effective_from` timestamptz NOT NULL, `created_at` timestamptz; FR-2.2)
- `events` (`id`, `user_id` FK → users, `text` varchar(500) NOT NULL, `local_date` DATE NOT NULL, `importance` int NOT NULL CHECK(1..5), `created_at` timestamptz NOT NULL, `updated_at` timestamptz, `deleted_at` timestamptz NULL — soft delete; `created_at` хранится как timestamptz (PostgreSQL всегда хранит в UTC); `local_date` — дата в TZ пользователя, тип DATE без времени)
- `summaries`
- `period_jobs`
- `delivery_attempts` — лог попыток доставки Telegram-сообщений (reminders FR-3, summary-уведомления, soft-reminders FR-6/FR-7). Колонки: `id` bigserial PK, `user_id` FK → users NOT NULL, `delivery_type` varchar NOT NULL (`reminder` | `summary_notification` | `soft_reminder`), `reference_id` bigint NULL (FK → prompt_deliveries.id для summary-related, NULL для reminders), `attempt_number` int NOT NULL DEFAULT 1, `status` varchar NOT NULL (`pending` | `sent` | `failed` | `terminal_failed`), `error_message` text NULL, `telegram_message_id` bigint NULL (ID отправленного сообщения), `scheduled_at` timestamptz NOT NULL, `sent_at` timestamptz NULL, `created_at` timestamptz NOT NULL DEFAULT now(). Индексы: `(user_id, delivery_type, scheduled_at)`, `(status) WHERE status IN ('pending', 'failed')` (partial, для retry processor). Retry policy: до 5 попыток с exponential backoff (NFR-1), запись на каждую попытку.
- `prompt_deliveries` (источник `sent_prompts` / `prompt_sent_at` для prompt→summary метрики)
- `admin_users` (id, email, password_hash, role, status, created_at)
- `admin_sessions` (id, admin_user_id FK → admin_users, token_hash, expires_at, created_at)
- `operation_id_cache` (client_operation_id dedupe, TTL 5 мин; PK или unique по `client_operation_id`; atomic insert: `INSERT ... ON CONFLICT DO NOTHING RETURNING`; может быть реализована через PostgreSQL таблицу с периодическим cleanup или Redis с TTL)
- `audit_logs` (id, actor_type, actor_id, action, target_type, target_id, payload, outcome, created_at)

## 4.2 Ключевые уникальные ограничения
- `users`: unique(`telegram_user_id`)
- `users_settings`: unique(`user_id`)
- `summaries`: unique(`user_id`, `period_type`, `period_start`, `period_end`)
- `period_run_counters`: unique(`user_id`, `period_type`, `period_start`, `period_end`)
- `period_jobs`: unique(`idempotency_key`)
- `events`: дедупликация пользовательских дублей через anti-double-submit (FR-9, FR-12 replay protection); на уровне БД — нет unique constraint на контент (пользователь может осознанно добавить несколько событий на один день). Поля `external_source`/`external_event_id` зарезервированы для будущих интеграций (вне scope MVP)
- `admin_users`: unique(lower(`email`)) — case-insensitive
- `week_schedule_history`: unique(`user_id`, `effective_from_local_date`)
- `operation_id_cache`: unique(`client_operation_id`)
- `prompt_deliveries`: unique(`prompt_id`) и unique(`user_id`, `period_type`, `period_start`, `period_end`, `sent_at`)

## 4.3 Жизненный цикл period_jobs и summaries

### period_jobs
Статусы: `pending → running → success | failed | retried | superseded`.
- `failed` при временной ошибке может перейти в `retried` и снова в `running`.
- `success`, `superseded` — терминальные.
- `superseded` — job был вытеснен более новым run (force re-run завершился раньше, см. 4.4.2).

### summaries
Статусы: `generating → generated | failed`.
- `generating` — summary создаётся (INSERT) в момент старта job с базовыми полями + status = `generating`, `content` = NULL.
- `generated` — job успешно завершён, `content` и `source_event_ids` заполнены.
- `failed` — job завершился ошибкой; запись summary **всегда существует** (создана при старте), `content` остаётся NULL. Это позволяет отличить «период ещё не обрабатывался» (нет записи) от «обработка провалилась» (запись с `failed`).
- При force re-run (FR-8.2b) существующий summary обновляется: status → `generating` → `generated`, version++.
- Summary создаётся **только при запуске job**; без job — нет записи summary.

### Partial success recovery (FR-8.3)
Если job завершился с ошибкой, но summary уже в статусе `generated`: повторный retry обнаруживает generated summary и завершается как `success` без пересчёта.

## 4.4 Concurrency model (one-run guarantee)
- Для ручных/авто запусков периодов winner определяется только БД (unique + transaction), не клиентом.
- **Авто-триггер:** после успешного `INSERT` события (отдельная транзакция), если выполнены условия FR-4.2, запускается транзакция создания period_job (ниже). Событие и period_job создаются в **разных транзакциях** (событие сохраняется независимо от успеха создания job).

### 4.4.1 Transaction: создание period_job (pseudocode)

```
BEGIN TRANSACTION

-- Шаг 1: Acquire lock на period_run_counters
INSERT INTO period_run_counters (user_id, period_type, period_start, period_end, last_run_number)
  VALUES (?, ?, ?, ?, 1)
  ON CONFLICT DO NOTHING;
row = SELECT * FROM period_run_counters
  WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?
  FOR UPDATE;

-- Шаг 2: Определить run_number
IF mode == auto_trigger (FR-8.2a):
    -- Проверка terminal fail: если job с текущим run_number в terminal failed → инкремент
    existing_job = SELECT status, attempt_count FROM period_jobs
      WHERE idempotency_key = hash(user_id, period_type, period_start, period_end, row.last_run_number);
    IF existing_job IS NOT NULL AND existing_job.status = 'failed' AND existing_job.attempt_count >= 3:
        UPDATE period_run_counters SET last_run_number = last_run_number + 1 WHERE ...;
        run_number = row.last_run_number + 1;  -- новый ключ, разблокировка периода
    ELSE:
        run_number = row.last_run_number;  -- без инкремента (штатный путь)
ELSE IF mode == force_rerun (FR-8.2b):
    UPDATE period_run_counters SET last_run_number = last_run_number + 1 WHERE ...;
    run_number = row.last_run_number + 1;

-- Шаг 3 (только авто-триггер): проверки
IF mode == auto_trigger:
    -- 3a: есть ли события в периоде?
    event_count = SELECT COUNT(*) FROM events
      WHERE user_id=? AND local_date BETWEEN period_start AND period_end
        AND deleted_at IS NULL;
    IF event_count == 0:
        ROLLBACK; RETURN "no_events";

    -- 3b: уже есть generated summary?
    existing = SELECT status FROM summaries
      WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?;
    IF existing.status == 'generated':
        ROLLBACK; RETURN "already_generated";

-- Шаг 4: создать period_job
idempotency_key = hash(user_id, period_type, period_start, period_end, run_number);
inserted = INSERT INTO period_jobs (idempotency_key, user_id, ..., status='pending')
  ON CONFLICT (idempotency_key) DO NOTHING
  RETURNING id;

IF inserted IS NULL:  -- conflict, job уже существует
    ROLLBACK; RETURN existing_job_result;

-- Шаг 5: создать/обновить summary, получить target_version
IF mode == force_rerun:
    -- Проверяем существование summary (для force нет шага 3b)
    existing_summary = SELECT version FROM summaries
      WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?;
    IF existing_summary IS NOT NULL:
        -- FR-14.3: force re-run при существующем summary разрешён даже при 0 событий
        UPDATE summaries SET status='generating', version=version+1
          WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?
          RETURNING version AS target_version;
    ELSE:
        -- FR-14.3: первый запуск при 0 событий → 400 empty_period
        event_count = SELECT COUNT(*) FROM events
          WHERE user_id=? AND local_date BETWEEN period_start AND period_end
            AND deleted_at IS NULL;
        IF event_count == 0:
            ROLLBACK; RETURN "400 empty_period";
        INSERT INTO summaries (user_id, ..., status='generating', version=1);
        target_version = 1;
ELSE:  -- auto_trigger
    INSERT INTO summaries (user_id, ..., status='generating', version=1)
      ON CONFLICT DO NOTHING;  -- защита от гонки
    -- Получить актуальную version (может быть > 1 после предыдущих force re-run + failed)
    current = SELECT version, status FROM summaries
      WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?;
    IF current.status IN ('failed', 'generating'):
        -- Recovery: обновить status для повторной обработки
        UPDATE summaries SET status='generating'
          WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?
            AND version = current.version;
        target_version = current.version;
    ELSE:
        target_version = current.version;  -- just inserted (version=1) или другой статус

-- Записать target_version в period_job для optimistic locking в worker
UPDATE period_jobs SET target_summary_version = target_version
  WHERE id = inserted.id;

COMMIT;
```

### 4.4.2 Worker: обработка period_job (pseudocode)

```
-- ═══ TX1: Claim job (короткая транзакция) ═══
BEGIN TRANSACTION;
lease_id = generate_uuid();
job = SELECT * FROM period_jobs WHERE status IN ('pending', 'retried')
  FOR UPDATE SKIP LOCKED LIMIT 1;
rows = UPDATE period_jobs
  SET status='running', started_at=now(), lease_id=lease_id,
      attempt_count = attempt_count + 1
  WHERE id=job.id AND status IN ('pending', 'retried');
IF rows == 0: ROLLBACK; SKIP;  -- уже забран другим worker'ом
COMMIT;
-- Row lock освобождён; reaper может проверять started_at этого job

-- target_version зафиксирован при создании job (шаг 5 в 4.4.1)
expected_version = job.target_summary_version;

-- ═══ FR-8.3: Partial success recovery (short-circuit, вне TX) ═══
existing = SELECT status FROM summaries
  WHERE user_id=? AND period_type=? AND period_start=? AND period_end=?
    AND version = expected_version;
IF existing.status == 'generated':
    UPDATE period_jobs SET status='success', finished_at=now()
      WHERE id=job.id AND lease_id=lease_id;
    RETURN;  -- done, no regeneration needed

-- ═══ Work: генерация content (вне транзакции, может быть долгой) ═══
content = generate_summary(job);  -- FR-14.1

-- ═══ TX2: Finalize (короткая транзакция) ═══
BEGIN TRANSACTION;
-- Fenced optimistic update: version + lease_id + status='generating'
rows_affected = UPDATE summaries s
  SET status='generated', content=?, source_event_ids=?, last_generated_at=now()
  FROM period_jobs j
  WHERE s.user_id = j.user_id AND s.period_type = j.period_type
    AND s.period_start = j.period_start AND s.period_end = j.period_end
    AND s.version = expected_version
    AND s.status = 'generating'
    AND j.id = job.id AND j.lease_id = lease_id;

IF rows_affected == 0:
    -- Summary был superseded или reaper вмешался
    UPDATE period_jobs SET status='superseded' WHERE id=job.id AND lease_id=lease_id;
ELSE:
    UPDATE period_jobs SET status='success', finished_at=now()
      WHERE id=job.id AND lease_id=lease_id;
COMMIT;
```

### 4.4.3 Stuck job recovery (timeout / reaper / fencing)

**Job timeout:** если `period_job` находится в статусе `running` дольше 5 минут (`now() - started_at > INTERVAL '5 minutes'`), он считается stuck.

**Fencing (lease_id):**
- При старте job worker генерирует уникальный `lease_id` (UUID) и записывает его в `period_jobs`:
  ```
  UPDATE period_jobs SET status='running', started_at=now(), lease_id=?
    WHERE id=? AND status='pending';
  ```
- Все последующие UPDATE от worker'а включают условие `AND lease_id = ?`:
  ```
  UPDATE period_jobs SET status='success', finished_at=now()
    WHERE id=? AND lease_id=?;
  ```
  Если reaper уже пометил job как `failed` и обнулил `lease_id` — zombie worker получит `rows_affected = 0` и прекратит работу.

**Reaper job:** фоновый процесс (cron, каждые 2 минуты) выполняет:
```
UPDATE period_jobs
  SET status = 'failed', finished_at = now(), lease_id = NULL,
      error = 'timeout: exceeded 5 min'
  WHERE status = 'running' AND started_at < now() - INTERVAL '5 minutes';
```

**Summary recovery:** для каждого timed-out job reaper обновляет связанный summary **только по target_summary_version job'а** (предотвращает clobber более новой генерации):
```
UPDATE summaries SET status = 'failed'
  WHERE user_id = job.user_id AND period_type = job.period_type
    AND period_start = job.period_start AND period_end = job.period_end
    AND status = 'generating'
    AND version = job.target_summary_version;
```
Это разблокирует авто-триггер (step 3b: `failed` ≠ `generated` → проходим дальше) и позволяет retry/force re-run. Если version уже сдвинулась (более новый force re-run) — reaper не трогает summary.

**Zombie worker protection (тройная):**
1. `lease_id` fencing на job: worker не может обновить `period_jobs`, если reaper сбросил `lease_id`.
2. `lease_id` fencing на summary: worker обновляет `summaries` через JOIN с `period_jobs` по `lease_id` — zombie с протухшим lease не пройдёт даже при совпадении version.
3. Optimistic locking summary version: worker не может обновить summary, если version сдвинулась (force re-run).

**Retry processor (совмещён с reaper cron, каждые 2 минуты):**
```
-- Выбрать failed jobs, готовые к retry
retryable = SELECT * FROM period_jobs
  WHERE status = 'failed'
    AND attempt_count < 3
    AND finished_at < now() - INTERVAL '30 seconds' * pow(2, attempt_count - 1)
  FOR UPDATE SKIP LOCKED LIMIT 10;

FOR EACH job IN retryable:
    new_lease = generate_uuid();
    UPDATE period_jobs
      SET status = 'retried', lease_id = NULL
      WHERE id = job.id;
```
- Worker забирает джобы в статусах `pending` **и** `retried`:
  ```
  job = SELECT * FROM period_jobs WHERE status IN ('pending', 'retried')
    FOR UPDATE SKIP LOCKED LIMIT 1;
  ```
  При старте worker инкрементирует `attempt_count` и устанавливает `lease_id`.

**Поле `attempt_count`** (integer, default 0) хранится в `period_jobs`. Инкрементируется worker'ом атомарно при claim (`attempt_count = attempt_count + 1` в UPDATE ... SET status='running'). После claim значение = номер текущей попытки (1, 2, 3). Максимум 3 attempts total. Retry processor фильтрует `WHERE attempt_count < 3`. При `attempt_count >= 3` и `status = 'failed'` — terminal failure. Backoff: `finished_at + 30s * 2^(attempt_count - 1)` (30s после 1-й, 60s после 2-й).

### 4.4.4 Гарантии
- **Lock-order:** counters → events check → jobs → summaries. Предотвращает deadlocks.
- **Optimistic locking** в worker (version check) предотвращает гонку force re-run vs. старый job.
- **Exit-on-conflict** на шаге 4 предотвращает порчу summary проигравшим авто-триггером.
- Повторные доставки webhook/клики не создают новый run при существующем `idempotency_key`.
- Статус `superseded` добавлен в state machine `period_jobs`: `pending → running → success | failed | retried | superseded`. `superseded` — терминальный.

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

## NFR-7. Data retention & user deletion contract
- `events` и `summaries` хранятся бессрочно (для MVP), пока пользователь не запросит удаление.
- Удаление пользователя: soft-delete сразу, hard-delete персональных данных в течение 30 дней.

**PII deletion contract (при hard-delete пользователя):**

| Таблица / поле | PII? | Действие при удалении |
|---|---|---|
| `users` (telegram_user_id, status) | Да | DELETE строка |
| `users_settings` (timezone, reminder_time, week_end) | Да (косвенно) | CASCADE DELETE (FK) |
| `events` (text, local_date, importance) | Да | DELETE все строки пользователя |
| `summaries` (content, source_event_ids) | Да (содержит копию текстов) | DELETE все строки пользователя |
| `period_jobs` (error) | Возможно (stack trace) | DELETE все строки пользователя |
| `period_run_counters` | Нет (только id/counts) | CASCADE DELETE (FK) |
| `delivery_attempts` | Нет (только статусы) | CASCADE DELETE (FK) |
| `prompt_deliveries` | Нет (только метрики) | Анонимизация: `user_id = NULL` (для сохранения агрегатов метрик) |
| `week_schedule_history` | Нет (только настройки) | CASCADE DELETE (FK) |
| `timezone_history` | Нет (только TZ/даты) | CASCADE DELETE (FK) |
| `audit_logs` (actor_id, payload) | Возможно | Анонимизация: `actor_id = NULL`, `payload = '{}'` для записей пользователя. Retention 180 дней — записи старше удаляются штатным cleanup. |
| `operation_id_cache` | Нет (TTL 5 мин) | Самоудаляется по TTL |
| `admin_sessions` / `admin_users` | N/A (не user data) | Не затрагивается |

**Порядок purge job:** `period_jobs` → `summaries` → `events` → `prompt_deliveries` (анонимизация) → `audit_logs` (анонимизация) → `delivery_attempts` → остальные FK-зависимые → `users_settings` → `users`. Выполняется в одной транзакции или с идемпотентным retry при частичном сбое.

**AC на удаление пользователя (SQL-инварианты после hard-delete):**
- `SELECT COUNT(*) FROM events WHERE user_id = ? → 0`
- `SELECT COUNT(*) FROM summaries WHERE user_id = ? → 0`
- `SELECT COUNT(*) FROM users WHERE id = ? → 0`
- `SELECT COUNT(*) FROM prompt_deliveries WHERE user_id = ? → 0` (user_id = NULL для анонимизированных)

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
   Для каждого запуска period-job корректно выставляются статусы state machine (pending/running/success/failed/retried/superseded).

17. **Auth security for user API**  
   Mini App API отклоняет невалидный или просроченный Telegram init data (`401`). Повторный запрос с тем же валидным `init_data_raw` в пределах replay TTL возвращает идемпотентный ответ (тот же session token, `200`), см. FR-12.1 и AC-20.

18. **Edge-cases covered**  
   Пройдены тесты: смена timezone, DST-переход, ручной запуск задним числом, `week_end` ≠ Sunday.

19. **Deterministic manual-period selection**  
   При manual run без выбора периода backend всегда выбирает один и тот же «последний полностью завершённый» период по правилам FR-5.

20. **Replay protection correctness**  
   Повторный запрос с тем же `init_data_raw` в пределах TTL возвращает тот же session token (idempotent). После истечения TTL — `401` (init data expired по `auth_date`). В параллельных запросах только один winner; остальные получают его результат.

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
Данный **PRD v2.13** считается базовой спецификацией MVP и передаётся в разработку. Приложение `METRICS.md` зафиксировано.
