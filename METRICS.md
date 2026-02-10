# METRICS.md — MVP Metrics Spec

## 1) Source of truth
- User activity: `events`, `summaries`
- Reminder delivery: `delivery_attempts`
- Prompt delivery (week/month/year prompts): `prompt_deliveries`
- Timezone basis: user IANA timezone
- Aggregation timezone for dashboard: UTC (с отдельной локальной расшифровкой при необходимости)

## 2) Definitions
- **Active user (day/week/month):** пользователь, у которого есть минимум 1 событие `events.created_at` в окне.
- **Reminder sent:** запись `delivery_attempts` со статусом `sent` для daily reminder.
- **Reminder converted:** reminder, после которого в течение 24 часов создано минимум 1 событие дня.
- **Prompt sent:** запись в `prompt_deliveries` со статусом `sent` и полями периода (`period_type`, `period_start`, `period_end`).
- **Prompt-to-summary converted:** prompt на период, после которого summary того же периода получил `status=success` в течение 48 часов.

## 3) Formulas
- **DAU (D):** count distinct `user_id` с событиями в день D.
- **WAU (W):** count distinct `user_id` с событиями за последние 7 дней (скользящее окно).
- **MAU (M):** count distinct `user_id` с событиями за последние 30 дней (скользящее окно).

- **Reminder→Event conversion:**
  `converted_reminders / sent_reminders` за период,
  где conversion window = 24h от `sent_at`.

- **Prompt→Summary conversion:**
  `successful_summaries / sent_prompts` за период,
  где conversion window = 48h от `prompt_sent_at`.

## 4) Dedup rules
- Повторные отправки одного и того же reminder с одинаковым `idempotency_key` считаются одной отправкой.
- Prompt dedup: одна отправка prompt определяется ключом (`user_id`, `period_type`, `period_start`, `period_end`, `sent_at`).
- Prompt→Summary: сопоставление one-to-one по (`user_id`, `period_type`, `period_start`, `period_end`) и окну 48h от `prompt_sent_at`.
- Повторные summary одного периода считаются одной конверсией (по уникальному ключу summary).

## 5) Freshness / SLA
- Dashboard обновляется не реже, чем раз в 15 минут.
- Расхождение между dashboard и raw-таблицами: не более 1% на тестовом датасете.

## 6) QA checks
- Формулы DAU/WAU/MAU сверяются на фиксированном тестовом наборе.
- Conversion-метрики сверяются на синтетических кейсах: late events, duplicate reminders, retries.
