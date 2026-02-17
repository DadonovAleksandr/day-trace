## Coverage Gaps (requirements not covered)
- Overall status: every top-level FR (FR-0..FR-14) has at least one related story, but several required clauses from FR/NFR/section 4 are not explicitly covered by any story.
- FR-0: Mini App first-open timezone auto-detection at registration time is not explicitly covered as backend behavior. `US-009` defaults to `UTC`; `US-046` mentions frontend detection, but no story guarantees that detected timezone is persisted during onboarding.
- FR-2.2: missing explicit coverage for open-period recalculation semantics after timezone change:
  - open weekly period recomputation with possible immediate completion and no retroactive auto-trigger;
  - open monthly/yearly period recomputation in new timezone.
  `US-020` covers cooldown/history/reminder recalculation, but not these period-state rules.
- FR-4.4: fallback rule not covered: when `local_date < min(effective_from_local_date)`, backend must use earliest `week_schedule_history` row. No AC in `US-021`/`US-022` mentions this.
- FR-8.1: idempotency key retention for at least 180 days is not represented in any story.
- FR-13.1 (`PUT /settings`): simultaneous `timezone + week_end` update order (timezone first, then week_end transition by new `today_local`) is missing.
- FR-13 (API contract completeness): no story explicitly requires endpoint-level request/response/error/idempotency specification artifact (e.g., OpenAPI), despite PRD requiring this for each MVP endpoint.
- Section 4.3 (summaries lifecycle): no explicit AC ensures that on non-timeout worker failure the summary remains as a persisted failed record (`status=failed`, `content=NULL`) so “failed vs never processed” is distinguishable.
- NFR-3: partial coverage only. There is no explicit story guaranteeing domain logs for reminder sends, period-job start/result/failure reason with correlation/job IDs (beyond generic logging setup in `US-002` and admin view in `US-057`).
- NFR-5: performance budgets are not covered (`API p95 <= 1.5s`, `summary generation p95 <= 2 min`, no manual refresh requirement).
- NFR-6: operational limits are not covered (10k MAU, 100k events/day, 20 rps peak) by capacity/load-test or guardrail stories.

## Stories Too Big (need splitting)
- `US-021` week_end transition logic is too broad. Split into:
  1. transition computation + history persistence,
  2. `409 transition_pending` lock behavior,
  3. catch-up job creation and empty catch-up auto-complete.
- `US-024` period job creation transaction is too large for one implementation pass. Split by execution mode:
  1. auto-trigger path,
  2. force re-run path,
  3. summary target_version creation/update semantics.
- `US-025` worker lifecycle is large. Split into:
  1. claim + attempt increment + lease fencing,
  2. partial-success/status recovery,
  3. finalize/supersede behavior.
- `US-037` reconciliation has many branching preconditions. Split into:
  1. candidate selection + precondition checks,
  2. recovery job creation path,
  3. reconcile-only (no new job) path.
- `US-046` Today screen mixes multiple features. Split into:
  1. list/view,
  2. create/backdate flow,
  3. edit/delete controls + 7-day gating,
  4. timezone detection handshake.
- `US-054` dashboard bundles backend metrics math + frontend rendering/refresh. Split API aggregation from UI visualization.
- `US-056` content management combines events and summaries with role/PII constraints. Split events view and summaries view (or backend API vs frontend page).
- `US-060` deletion contract is very large. Split into:
  1. soft-delete workflow,
  2. hard-delete/anonymization job,
  3. SQL invariant verification tests.
- `US-063` deployment setup bundles multiple deployables. Split backend containerization, frontend containerization, and compose/runtime wiring.
- `US-065` and `US-066` test stories are oversized. Split by domain (auto-trigger/idempotency, reaper/retry, auth/replay, timezone/week_end/DST).

## Redundant Stories (should merge)
- `US-017` and `US-061`: cleanup responsibility overlaps (`US-017` already includes periodic cleanup AC for operation-id cache).
- `US-058` and `US-062`: audit retention cleanup is duplicated (retention behavior appears in both).
- `US-028` and `US-029`: nearly identical auto-trigger logic by period type; can be a single parameterized story for calendar periods (month/year).
- `US-009` and `US-042`: both include `/start` registration behavior; reduce duplication by keeping registration logic in one backend story and bot UX in the other.

## Acceptance Criteria Issues
- `US-009`: uses `POST /start` wording, but PRD defines `/start` as bot command. AC should specify command handling path and expected backend side effects.
- `US-009`: does not test Mini App onboarding timezone persistence from `Intl` (FR-0 critical behavior).
- `US-010`/`US-011`: AC does not assert required evaluation order (`auth_date` max-age check before replay check).
- `US-012`: “renews token expiry” is ambiguous; AC should specify whether token is rotated and how client receives refreshed expiry.
- `US-019`: missing explicit `401 unauthorized` AC for protected endpoint.
- `US-020`: lacks explicit acceptance checks for open weekly/monthly/yearly recalculation rules and non-retroactive trigger behavior.
- `US-021`: “auto-completed” transition is vague; AC should define persisted state/criteria that mark completion.
- `US-024`: “implements pseudocode faithfully” is not directly testable. Replace with concrete DB-state assertions per branch.
- `US-025`: similar testability issue; AC should include explicit status transitions and fenced update assertions.
- `US-028`/`US-029`: AC misses “once per run_number” wording and terminal-failure recovery linkage from FR-8.2a.
- `US-031`: missing explicit idempotent dedupe response behavior (`200` with identical body for same `X-Client-Operation-Id`) and `invalid_period` branch.
- `US-042`/`US-044`: “user-friendly error/progress” is subjective; define exact messages/states or structured response mapping.
- `US-054`: hardcoded DAU/WAU/MAU formulas may diverge from `METRICS.md`; AC should reference single source-of-truth formulas only.

## Other Recommendations
- Add a traceability matrix in `prd.json` (`requirementIds: [...]` on each story) to make FR/NFR/4.x coverage auditable automatically.
- Add explicit dependencies (`dependsOn`) so orchestration can schedule stories safely (e.g., `US-024` depends on `US-023`, `US-005`, `US-006`).
- Standardize AC format to Given/When/Then with explicit status codes and DB invariants for backend stories.
- Add dedicated performance/capacity stories for NFR-5/NFR-6 (load profiles, pass/fail thresholds, measurement method).
- Add one story for API contract publication/validation (OpenAPI + schema tests) to satisfy FR-13 contract completeness.
