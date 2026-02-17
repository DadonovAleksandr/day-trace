# Ревью декомпозиции PRD → User Stories (Opus)

**Проект:** DayTrace MVP  
**Стек:** C# .NET 8+ / Vue.js 3 / PostgreSQL / NLog  
**Документы:** PRD v2.16, prd.json (66 stories), review-prd.md (Codex)  
**Дата:** 2026-02-17  

---

## Согласен с Codex

### Coverage Gaps — подтверждаю

1. **FR-0: Timezone auto-detection не доведён до бэкенда.** US-046 детектирует TZ через `Intl.DateTimeFormat()`, US-009 регистрирует с `timezone=UTC`. Между ними нет AC, который гарантирует: «Mini App при первом открытии отправляет детектированный TZ → бэкенд персистит его в `users_settings`». Это критичный пробел — без него все пользователи, пришедшие через Mini App, останутся с UTC.

2. **FR-2.2: Пересчёт открытых периодов после смены TZ.** US-020 покрывает cooldown, history, reminder, но не покрывает: (а) пересчёт границ открытого weekly-периода, (б) отсутствие ретроактивного авто-триггера при сдвиге `today_local`, (в) пересчёт monthly/yearly в новой TZ. Нужны дополнительные AC в US-020.

3. **FR-4.4: Fallback-правило `week_schedule_history`.** `local_date < min(effective_from_local_date)` → используем earliest запись. Не покрыто ни в US-022 (date calculations), ни в US-032. Добавить AC в US-022.

4. **FR-8.1: Retention idempotency key 180 дней.** Нигде не зафиксирован. Добавить AC в US-023.

5. **FR-13.1: Одновременная смена timezone + week_end.** PRD явно указывает порядок: «сначала timezone (обновляется `today_local`), затем `week_end` (transition вычисляется по новой TZ)». Ни US-020, ни US-021 это не покрывают. Нужен явный AC — предлагаю в US-020 (как основной обработчик PUT /settings).

6. **Section 4.3: Summary с status=failed как persisted record.** Не хватает AC, что при сбое worker'а summary остаётся в БД с `status=failed`, `content=NULL`. Это отличает «не обрабатывался» от «сломался». Добавить AC в US-025.

### AC Issues — подтверждаю

7. **US-009: «POST /start» — неверная формулировка.** `/start` — это Telegram bot command, не HTTP endpoint. AC должен описывать обработку bot-команды с вызовом domain-сервиса регистрации.

8. **US-010 / US-011: Порядок проверок.** PRD явно требует: `auth_date` max age → replay protection. AC должен фиксировать этот порядок.

9. **US-012: «Renews token expiry» — неоднозначно.** Ротация JWT? Sliding expiration в claims? Refresh token? Sub-агент не сможет принять архитектурное решение. AC должен специфицировать механизм (рекомендую: новый JWT с обновлённым `exp` в response header при каждом успешном запросе).

10. **US-024: «Implements pseudocode faithfully» — нетестируемо.** Заменить на конкретные DB-state assertions: «после auto-trigger с 0 событий → ROLLBACK, нет записей в `period_jobs`», «после force re-run → `last_run_number` инкрементирован» и т.д.

11. **US-031: Пропущены ошибки.** Нет AC для `400 invalid_period` (некорректные даты) и явного idempotent dedupe behaviour (200 с тем же телом при повторном `X-Client-Operation-Id`).

12. **US-028 / US-029: Пропущена связь с FR-8.2a.** AC не упоминают «once per run_number» и terminal-failure recovery. Добавить.

### Redundant Stories — подтверждаю частично

13. **US-017 ↔ US-061.** US-017 AC уже включает «Periodic cleanup of expired entries (older than 5 min)». US-061 дублирует. **Merge US-061 в US-017** (добавить детали: batch delete с LIMIT, логирование количества).

14. **US-009 ↔ US-042.** Оба затрагивают `/start`. **Решение:** US-009 = domain-сервис регистрации (без привязки к боту). US-042 = bot command handler, вызывает сервис из US-009. Убрать bot-специфику из US-009.

### Other Recommendations — подтверждаю

15. **Dependencies (`dependsOn`).** Критически важно для оркестрации sub-агентов. Например: US-024 зависит от US-005, US-006, US-023; US-027 зависит от US-024, US-025. Без этого параллелизация невозможна.

16. **NFR-5: Performance budgets.** Нужна хотя бы одна story: «API p95 ≤ 1.5s на тестовом наборе, summary generation p95 ≤ 2 min, измерение + fix узких мест».

---

## Не согласен с Codex (с обоснованием)

### 1. Splitting US-024 (period job creation transaction) — **против**

Codex предлагает разбить на 3: auto-trigger path, force re-run path, summary version semantics.

**Обоснование:** Транзакция из 4.4.1 — единый атомарный блок с ветвлением по `mode`. Это один метод/сервис (~80-120 строк C#), который реализует pseudocode из PRD. Разделение по mode создаёт искусственные границы:
- Auto-trigger и force-rerun разделяют шаги 1, 4, 5 (lock, insert job, create/update summary).
- Summary version semantics невозможно отделить от creation — version определяется в той же транзакции.

Sub-агент получает pseudocode из PRD и реализует его целиком. Это **одна** цельная единица работы. Разбиение приведёт к трём stories, каждая из которых неполна без остальных.

**Рекомендация:** Оставить US-024 как есть. Заменить «implements pseudocode faithfully» на конкретные assertions (см. п.10 выше).

### 2. Splitting US-037 (terminal failure reconciliation) — **против**

Codex предлагает 3 parts: candidate selection, recovery job, reconcile-only path.

**Обоснование:** Reconciliation — один cron handler (~60 строк). «Candidate selection + precondition checks» и «recovery job creation» — это шаги одного алгоритма, не отдельные deliverables. «Reconcile-only path» (просто пометить `reconciled_at`) — одна строка в else-ветке. Три stories по 2-3 AC каждая — это overhead без пользы.

**Рекомендация:** Оставить US-037 как есть. AC уже покрывают все ветки.

### 3. Splitting US-025 (worker) на 3 части — **избыточно**

Codex: claim + attempt, partial-success recovery, finalize/supersede.

**Обоснование:** Worker lifecycle — стандартный паттерн claim → execute → finalize. Pseudocode из 4.4.2 — единый flow. Partial success recovery — это один IF в начале (short-circuit). Supersede — один IF в конце. Разбиение на 3 создаёт stories, каждая из которых не работает самостоятельно.

**Рекомендация:** Оставить US-025 как одну story. AC конкретизировать (заменить «proper fencing» на «UPDATE с `WHERE lease_id = ?` возвращает 0 rows → status=superseded»).

### 4. Splitting US-046 (Today screen) на 4 части — **слишком мелко**

Codex: list/view, create/backdate, edit/delete, timezone detection.

**Обоснование:** Это один экран Vue.js. List + Create — основной flow, неразделимый в UI. Edit/Delete — те же компоненты с флагами. Timezone detection — одна строка JS (`Intl.DateTimeFormat`) + один API call. Четыре stories для одного экрана — это 4 PR, 4 ревью, 4 деплоя для одной страницы.

**Рекомендация:** Разбить максимум на 2: (1) Today screen (list + create + edit/delete), (2) Mini App Vue.js shell + Telegram SDK + timezone detection (см. дополнительные находки, п.1). Но не на 4.

### 5. Merging US-028 + US-029 (monthly + yearly triggers) — **против**

Codex предлагает объединить как «параметризованную story для calendar periods».

**Обоснование:** 
- Trigger conditions различаются: «последний день месяца в TZ» vs «Dec 31 в TZ». Это разная логика определения trigger day.
- Soft reminder messages различаются (monthly vs yearly).
- Каждый trigger — отдельная точка тестирования.
- Объединённая story получит 12+ AC и два отдельных trigger flow. Это не упрощение, а усложнение для sub-агента.

**Рекомендация:** Оставить отдельными. Пометить в notes: «реализация переиспользует общий сервис `PeriodAutoTriggerService` с параметром `periodType`».

### 6. Splitting US-063 (deployment) — **против**

Codex: backend container, frontend container, compose/wiring.

**Обоснование:** Docker/deployment — это конфигурация, не код. Dockerfile backend (20 строк) + Dockerfile frontend (15 строк) + docker-compose.yml (40 строк) + README — всё это один deliverable. Sub-агент создаёт все файлы за один проход. Три stories для ~100 строк конфига — overhead.

**Рекомендация:** Оставить US-063 как есть.

### 7. NFR-6 не требует отдельной story — **частично**

Codex предлагает capacity/load-test stories для NFR-6 (10k MAU, 100k events/day, 20 rps).

**Обоснование:** Для MVP это ориентировочные лимиты, не SLA. Load testing — это operations concern, не development story. Архитектурные решения (connection pooling, pagination) уже заложены в существующие stories. Отдельная capacity story на этапе MVP — premature optimization.

**Рекомендация:** Не добавлять story. Убедиться, что существующие AC включают pagination limits, connection pooling, batch processing.

### 8. API contract artifact (OpenAPI) — **не нужен как отдельная story**

Codex предлагает story для OpenAPI + schema tests.

**Обоснование:** В .NET OpenAPI генерируется из кода (Swashbuckle/NSwag). Каждая endpoint-story уже определяет request/response/error contract. Отдельная story для «генерации OpenAPI» — формальность. Schema tests полезны, но покрываются интеграционными тестами (US-064..066).

**Рекомендация:** Добавить AC в US-001 (scaffolding): «Swagger/OpenAPI middleware configured, `/swagger` endpoint available in dev».

### 9. US-058 ↔ US-062 — **НЕ дублируются**

Codex говорит audit retention overlap.

**Обоснование:** US-058 — это **UI для просмотра** audit logs (frontend + API). US-062 — это **background job для очистки** записей старше 180 дней. Это разные concerns: read vs. cleanup. UI может показывать «retention: 180 days» label, но реализация очистки — отдельный background job.

**Рекомендация:** Оставить как есть. Убрать «180-day retention» из AC US-058 (это не ответственность UI) и оставить только в US-062.

---

## Дополнительные находки

### 1. Missing: Mini App Vue.js shell + Telegram SDK setup

Есть US-059 «Admin UI — Vue.js SPA shell» для админки, но **нет аналога для Mini App**. Mini App нуждается в:
- Vue.js project scaffolding (Vite + Vue 3)
- Telegram Mini App SDK integration (`@tma.js/sdk` или `@telegram-apps/sdk`)
- Auth flow: при открытии → `POST /auth/telegram` → store session token
- Navigation (5 экранов)
- Telegram theme integration (цвета, viewport, back button)
- API client с автоматической подстановкой auth token

Без этой story US-046..050 (экраны) не имеют основы.

**Рекомендация:** Добавить **US-067** «Mini App — Vue.js SPA shell with Telegram SDK» (priority 7, перед US-046..050).

### 2. Missing: Soft reminders (FR-6, FR-7)

PRD FR-6: «Если в последний день месяца пользователь не добавил ни одного события, авто-формирование не запускается; пользователь получает **мягкое напоминание** о ручном запуске monthly summary.»  
PRD FR-7: Аналогично для yearly.

US-028 AC упоминает «soft reminder about manual run», но нет story, которая реализует отправку этих напоминаний. Кто их шлёт? Когда? Через Bot API?

**Рекомендация:** Добавить AC в US-038 (daily reminder scheduler) или создать отдельную story: «Period-end soft reminders — если авто-триггер не сработал на `week_end`/last day of month/Dec 31, отправить мягкое напоминание через Bot на следующий день».

### 3. US-033: «Only returns summaries in status 'generated'» — спорно

AC говорит: показывать только `generated`. Но пользователю полезно видеть:
- `generating` — «ваш summary формируется» (чтобы не жать кнопку повторно)
- `failed` — «произошла ошибка, попробуйте снова»

Если API возвращает только `generated`, Mini App не может показать статус in-progress или ошибку.

**Рекомендация:** Изменить AC: «Returns summaries in status `generated` by default. Query param `include_status` (optional, comma-separated) allows including `generating` and `failed`.» Или проще: возвращать все статусы, пусть UI решает что показывать.

### 4. US-013: X-Client-Operation-Id «required» — уточнить scope

AC говорит header required, но он нужен только для мутирующих операций (POST, PATCH, DELETE). GET endpoints не должны его требовать. US-017 (dedupe middleware) тоже не уточняет.

**Рекомендация:** AC US-017 дополнить: «Required only for POST, PATCH, DELETE endpoints. GET requests bypass dedupe.»

### 5. US-012: JWT vs opaque token — нужно решение

PRD говорит «JWT или opaque». US-012 AC предполагает JWT. Это архитектурное решение, которое влияет на:
- Renewal mechanism (JWT: новый токен vs opaque: update expiry в БД)
- Token validation (JWT: stateless vs opaque: DB lookup)
- Token size (JWT ~500 bytes vs opaque ~32 bytes)

Для Telegram Mini App (с sliding expiration) opaque token проще: просто `UPDATE sessions SET expires_at = now() + 24h WHERE token_hash = ?`. JWT требует rotation при каждом запросе.

**Рекомендация:** Зафиксировать решение в AC: «Session token = opaque (UUID), stored in `sessions` table. Validation = DB lookup. Renewal = UPDATE expires_at on activity.» Если JWT — описать rotation mechanism.

### 6. Missing: CORS configuration

Mini App (Telegram WebView) и Admin UI (browser) вызывают API с другого origin. Без CORS middleware запросы будут блокироваться.

**Рекомендация:** Добавить AC в US-001 (scaffolding): «CORS middleware configured. Allowed origins configurable via env (ALLOWED_ORIGINS). Default: Telegram Mini App origin + Admin UI origin.»

### 7. US-054: METRICS.md как prerequisite

PRD Section 6 требует: «До старта разработки должен быть зафиксирован отдельный METRICS.md с формулами.» US-054 ссылается на «Formulas match METRICS.md exactly», но METRICS.md не создаётся ни одной story.

**Рекомендация:** Либо добавить prerequisite story «Create METRICS.md with DAU/WAU/MAU formulas per PRD section 6» (priority 1, non-code), либо зафиксировать формулы прямо в AC US-054.

### 8. US-015/US-016: 403 vs 422 для expired edit window

AC говорят «403 if expired» для попытки редактирования/удаления после 7 дней. 403 Forbidden обычно означает «нет прав». Более корректно: 409 Conflict или 422 Unprocessable Entity (бизнес-правило нарушено, а не auth).

**Рекомендация:** Использовать `422 edit_window_expired` с сообщением «Editing is allowed only within 7 days of creation». Или оставить 403, но задокументировать что это business rule, не auth issue.

### 9. US-021: «auto-completed» transition — нужна конкретика

Codex тоже отметил. AC: «Empty catch-up transition → auto-completed, no block on future changes». Что значит «auto-completed»? Какое состояние в БД?

**Рекомендация:** AC уточнить: «If catch-up transition period has 0 events → no `period_job` created, no `summary` record. Transition is considered completed: subsequent `PUT /settings` with new `week_end` will NOT return `409 transition_pending`.» Добавить: check condition = «summary exists with status `generated` OR no non-deleted events in transition period».

### 10. Priority sequencing: US-022 (date calculations) должна быть priority 2, не 3

US-022 (timezone-aware date calculations) — фундамент для US-013 (create event, нужно `today_local`), US-014 (list events, нужен date filter в TZ), US-020 (timezone change). Все они priority 3-4, но зависят от US-022.

**Рекомендация:** Поднять US-022 до priority 2.

### 11. Test stories (US-064..066) — слишком крупные, но дробить иначе

Codex предлагает «split by domain». Согласен что крупные, но предлагаю другой принцип дробления:

- **US-064** (event lifecycle) — OK как есть, ~8 test cases.
- **US-065** (summary generation + concurrency) — разбить на 2:
  - **US-065a:** Auto-triggers + manual run (happy path): 4 AC
  - **US-065b:** Concurrency + terminal failure + reaper + retry: 4 AC
- **US-066** (auth + timezone + week_end) — OK как есть, ~8 test cases.

### 12. Нет story для error handling middleware

Нет unified error response format. PRD определяет конкретные error codes (`400 validation_error`, `400 date_out_of_range`, `409 transition_pending`, `429 timezone_change_cooldown`), но нет story для error handling middleware, который:
- Форматирует все ошибки в единую JSON-структуру `{error, message, details?}`
- Логирует ошибки через NLog с correlation ID
- Обрабатывает unhandled exceptions (500) с safe error message

**Рекомендация:** Добавить AC в US-002 (NLog) или US-001 (scaffolding): «Global exception handler middleware: all errors return `{error: string, message: string}` JSON. Unhandled exceptions → 500 with `internal_error`, logged with correlation ID. No stack traces in response.»

---

## Итоговые рекомендации (финальный список правок для prd.json)

### Новые stories

| ID | Title | Priority | Обоснование |
|---|---|---|---|
| US-067 | Mini App — Vue.js SPA shell with Telegram SDK | 7 | Основа для US-046..050. Scaffolding, SDK, auth flow, navigation, theme. |

### Merge

| Действие | Stories | Результат |
|---|---|---|
| Merge | US-061 → US-017 | Добавить в US-017 AC: batch delete с LIMIT, логирование. Удалить US-061. |

### AC Changes — обязательные

| Story | Изменение |
|---|---|
| US-001 | Добавить AC: «CORS middleware configured (origins via env)», «Swagger/OpenAPI endpoint in dev» |
| US-001 | Добавить AC: «Global error handling middleware: `{error, message}` JSON, 500 safe response, NLog correlation ID» |
| US-009 | Исправить «POST /start» → «Bot `/start` command handler calls `UserRegistrationService.RegisterAsync()`» |
| US-009 | Добавить AC: «When called from Mini App with detected timezone → persists timezone (not UTC)» |
| US-009 | Убрать bot-UX детали (one-time hint) — оставить в US-042 |
| US-010 | Добавить AC: «Evaluation order: auth_date max age check (5 min) → replay protection check» |
| US-012 | Специфицировать token type: opaque (UUID, DB-backed) или JWT (с rotation mechanism). Убрать неоднозначность. |
| US-013 | Уточнить: «X-Client-Operation-Id required for POST/PATCH/DELETE only» |
| US-015 | Рассмотреть: `422 edit_window_expired` вместо `403` (или зафиксировать 403 как design decision) |
| US-016 | Аналогично US-015 |
| US-017 | Добавить AC из US-061 (batch cleanup, logging). Добавить: «Applies only to POST, PATCH, DELETE» |
| US-020 | Добавить AC: «Open weekly period boundaries recalculated per `week_schedule_history`; no retroactive auto-trigger» |
| US-020 | Добавить AC: «When `timezone` and `week_end` both present in request → timezone applied first, then week_end transition computed from new `today_local`» |
| US-021 | Уточнить «auto-completed»: «Transition with 0 events → no summary/job created, does not block future `week_end` changes. Check: `(summary.status == generated) OR (non-deleted event count in [transition_start, transition_end] == 0)`» |
| US-022 | Добавить AC: «Fallback: if `local_date < min(effective_from_local_date)` → use earliest `week_schedule_history` record» |
| US-022 | **Priority 2 → поднять с 3** (фундамент для событий и периодов) |
| US-023 | Добавить AC: «Idempotency keys retained for minimum 180 days (FR-8.1)» |
| US-024 | Заменить «implements pseudocode faithfully» → конкретные DB-state assertions для каждой ветки (auto-trigger 0 events → rollback; force re-run → increment; terminal recovery → new key) |
| US-025 | Добавить AC: «On non-timeout failure, summary persists with `status=failed`, `content=NULL`» |
| US-025 | Конкретизировать fencing: «Worker UPDATE includes `WHERE lease_id = ?`; if 0 rows → set job `superseded`» |
| US-028 | Добавить AC: «Once per run_number (FR-8.2a). Terminal failure → increment run_number, create new job» |
| US-029 | Аналогично US-028 |
| US-031 | Добавить AC: «400 `invalid_period` for malformed dates» |
| US-031 | Добавить AC: «Dedupe: repeat `X-Client-Operation-Id` within 5 min → 200 with same response body» |
| US-033 | Изменить: «Returns summaries in all statuses (`generated`, `generating`, `failed`). Status included in response object.» |
| US-042 | Добавить: «/start welcome message includes one-time timezone hint if `timezone=UTC`» (перенести из US-009) |
| US-054 | Добавить prerequisite: «METRICS.md must exist with formulas before implementation» или встроить формулы в AC |
| US-058 | Убрать «180-day retention» из AC (это ответственность US-062, не UI) |

### AC Changes — рекомендуемые (nice-to-have)

| Story | Изменение |
|---|---|
| US-042/044 | Заменить «user-friendly error» на конкретные Telegram messages с шаблонами |
| US-054 | Разбить на 2: backend API (GET /metrics/dashboard) + frontend visualization |
| US-065 | Разбить на 2: (a) happy path triggers/manual run, (b) concurrency/failure/recovery |

### Удалить

| Story | Причина |
|---|---|
| US-061 | Merged into US-017 |

### НЕ делать (вопреки Codex)

| Предложение Codex | Причина отклонения |
|---|---|
| Split US-024 на 3 | Единая транзакция, нельзя реализовать по частям |
| Split US-025 на 3 | Единый worker lifecycle, ~100 строк кода |
| Split US-037 на 3 | Один cron handler с ветвлением, ~60 строк |
| Split US-046 на 4 | Один экран, слишком мелкое дробление |
| Split US-063 на 3 | ~100 строк конфигурации |
| Merge US-028 + US-029 | Разные trigger conditions, отдельные test cases |
| Merge US-058 + US-062 | Разные concerns: UI vs background cleanup |
| Story для OpenAPI artifact | Генерируется из кода, добавить setup в US-001 |
| Story для NFR-6 capacity | Premature для MVP, лимиты — ориентиры, не SLA |
| Given/When/Then формат | Текущий формат достаточно конкретен; переформатирование 66 stories — overhead без пользы |
| Traceability matrix в prd.json | Overhead; `notes` поле уже ссылается на FR. Достаточно. |

### Dependencies (добавить поле `dependsOn` в prd.json)

Ключевые цепочки для оркестрации sub-агентов:

```
US-001 → US-002, US-003
US-003 → US-004, US-005, US-006, US-007
US-004 → US-009, US-013..016, US-022
US-005 → US-023, US-024, US-026
US-006 → US-021, US-038
US-008 → US-009, US-042..045
US-010 → US-011, US-012
US-012 → US-013..016, US-018..020, US-031, US-033
US-022 → US-027..029, US-032
US-023 → US-024
US-024 → US-025, US-027..029, US-031
US-025 → US-035..037
US-026 → US-025
US-059 → US-054..058
US-067(new) → US-046..050
```

---

### Итого: 66 stories → 66 stories (−1 merge + 1 new = 66)

Декомпозиция в целом **качественная** — покрытие PRD близко к полному, granularity адекватная для sub-агентов. Основные проблемы не в структуре, а в **конкретике AC** (нетестируемые формулировки, пропущенные error codes, неспецифицированные edge cases). Codex нашёл большинство реальных проблем, но переусердствовал с дроблением — 10 предложений split создали бы ~25 дополнительных stories, большинство из которых нежизнеспособны по отдельности.
