# MVP Metrics Spec

> Статус на `2026-02-23`: после перехода на manual highlight flow (`dbc620d`) таблица `prompt_deliveries` удалена.
> Prompt→Summary conversion в текущем dashboard временно отключена и возвращается как `0/0` (см. `MetricsRepository.GetPromptConversionAsync`), пока не будет определена новая формула для highlight-based flow.

## 1) Source of truth
- User activity: `events`
- Reminder delivery: `delivery_attempts`
- Summary completion: `summaries`
- Aggregation timezone for dashboard: UTC (`DateTime.UtcNow` in `AdminMetricsController`)

## 2) Definitions
- **Active user (DAU/WAU/MAU):** пользователь, у которого есть минимум 1 событие (`events`) в окне, с фильтром `events.deleted_at IS NULL`.
- **Reminder sent:** запись `delivery_attempts` c `delivery_type='reminder'`, `status='sent'`, `sent_at` в окне 24h.
- **Reminder converted:** reminder-сообщение, после которого у этого же `user_id` есть хотя бы 1 событие в `events` в течение 24h от `sent_at`.
- **Prompt sent / Prompt-to-summary converted:** метрика временно отключена в текущей реализации (нет `prompt_deliveries`; dashboard отдаёт `converted=0`, `total=0`).

## 3) Formulas
- **DAU (D):** `count(distinct user_id)` по `events.created_at` в `[D 00:00 UTC, D+1d)` и `deleted_at IS NULL`.
- **WAU (W):** `count(distinct user_id)` по `events.created_at` в `[date_trunc('day', as_of)-7d, as_of]` и `deleted_at IS NULL`.
- **MAU (M):** `count(distinct user_id)` по `events.created_at` в `[date_trunc('day', as_of)-30d, as_of]` и `deleted_at IS NULL`.

- **Reminder→Event conversion:**
  `converted_reminders / sent_reminders` за период,
  где conversion window = 24h от `sent_at`.

- **Prompt→Summary conversion (historical/disabled):**
  формула ниже была привязана к `prompt_deliveries`, но сейчас не применяется в runtime-метриках.

## 4) Practical mapping: metric -> tables/fields

| Метрика | Таблицы | Поля / фильтры |
|---|---|---|
| DAU / WAU / MAU | `events` | `user_id`, `created_at`, `deleted_at IS NULL` |
| Reminder sent / conversion | `delivery_attempts`, `events` | `delivery_attempts.delivery_type='reminder'`, `delivery_attempts.status='sent'`, `delivery_attempts.sent_at`; join/exists по `events.user_id`, окно `events.created_at` от `sent_at` до `sent_at + 24h`, `events.deleted_at IS NULL` |
| Prompt sent / conversion | `N/A (disabled)` | В текущей реализации metric отключена; API dashboard возвращает `0/0` до появления новой схемы расчёта для highlight-based flow |

## 5) SQL checks (PostgreSQL)

### 5.1 DAU / WAU / MAU
```sql
WITH params AS (
  SELECT
    DATE '2026-02-18' AS target_day,
    TIMESTAMPTZ '2026-02-18 12:00:00+00' AS as_of
)
SELECT
  (
    SELECT COUNT(DISTINCT e.user_id)
    FROM events e, params p
    WHERE e.deleted_at IS NULL
      AND e.created_at >= p.target_day::timestamptz
      AND e.created_at < (p.target_day::timestamptz + INTERVAL '1 day')
  ) AS dau,
  (
    SELECT COUNT(DISTINCT e.user_id)
    FROM events e, params p
    WHERE e.deleted_at IS NULL
      AND e.created_at >= date_trunc('day', p.as_of) - INTERVAL '7 days'
      AND e.created_at <= p.as_of
  ) AS wau,
  (
    SELECT COUNT(DISTINCT e.user_id)
    FROM events e, params p
    WHERE e.deleted_at IS NULL
      AND e.created_at >= date_trunc('day', p.as_of) - INTERVAL '30 days'
      AND e.created_at <= p.as_of
  ) AS mau;
```

### 5.2 Reminder conversion (24h)
```sql
WITH params AS (
  SELECT TIMESTAMPTZ '2026-02-18 12:00:00+00' AS as_of
),
reminders AS (
  SELECT d.id, d.user_id, d.sent_at
  FROM delivery_attempts d, params p
  WHERE d.delivery_type = 'reminder'
    AND d.status = 'sent'
    AND d.sent_at >= p.as_of - INTERVAL '24 hours'
    AND d.sent_at <= p.as_of
),
converted AS (
  SELECT r.id
  FROM reminders r
  WHERE EXISTS (
    SELECT 1
    FROM events e
    WHERE e.user_id = r.user_id
      AND e.deleted_at IS NULL
      AND e.created_at >= r.sent_at
      AND e.created_at <= r.sent_at + INTERVAL '24 hours'
  )
)
SELECT
  (SELECT COUNT(*) FROM converted) AS converted,
  (SELECT COUNT(*) FROM reminders) AS total,
  ROUND(
    (SELECT COUNT(*)::numeric FROM converted)
    / NULLIF((SELECT COUNT(*) FROM reminders), 0),
    4
  ) AS rate;
```

### 5.3 Prompt conversion (48h)
```sql
SELECT
  0 AS converted,
  0 AS total,
  0::numeric AS rate;
```
Примечание: исторический SQL через `prompt_deliveries` удалён из актуальной спецификации, т.к. таблица удалена из схемы.

## 6) Dedup rules (actual schema + code)
- Для `delivery_attempts` **нет** поля `idempotency_key`; reminder dedup не строится на этом ключе.
- Reminder dedup в runtime: перед созданием нового reminder проверяется `delivery_attempts` по (`user_id`, `delivery_type='reminder'`, `scheduled_at` в границах суток пользователя), с условием `status != 'terminal_failed'`.
- Повторные попытки отправки reminder идут через обновление той же строки (`attempt_number`, `status`, `sent_at`), а не через новый insert.
- Prompt dedup / Prompt→Summary conversion dedup: не применяется в текущей реализации (метрика отключена до новой модели).

## 7) Freshness / SLA
- Dashboard обновляется не реже, чем раз в 15 минут.
- Расхождение между dashboard и raw-таблицами: не более 1% на тестовом датасете.

## 8) QA checks
- Формулы DAU/WAU/MAU сверяются на фиксированном тестовом наборе.
- Reminder conversion сверяется на синтетических кейсах: late events, duplicate reminders, retries.
- Prompt→Summary conversion QA временно не применяется (метрика отключена).
