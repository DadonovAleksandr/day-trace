# План: Добавить выбор highlight-события на экранах Месяца и Года

## Контекст

На экране **Недели** (`WeekView.vue`) уже реализован ручной выбор главного события (highlight): пользователь видит карточки 7 дней, тапом выбирает одну, нажимает «Сохранить» → `PUT /summaries/weekly/highlight`. Блокировка: если есть monthly summary → weekly заблокирован (замок).

Нужно реализовать аналогичную механику на экранах **Месяца** и **Года**.

---

## Общие принципы (уже реализованы, не требуют изменений)

- **Backend** полностью готов: `PUT /summaries/{periodType}/highlight` принимает `periodType` = `weekly | monthly | yearly`. `HighlightService` уже обрабатывает все три типа.
- **`EventLockService`** уже реализует полную иерархию блокировок: weekly → locked by monthly, monthly → locked by yearly, yearly → never locked.
- **`isSummaryLocked`** в `useLockCheck.ts` уже обрабатывает `monthly` (проверяет yearly).
- **API** `setHighlight(periodType, { event_id, period_start, period_end })` — без изменений.
- **Тип `Summary`** уже содержит `highlight_event_id: string | null`.

**Вывод: бэкенд НЕ трогаем. Все изменения только на фронтенде.**

---

## Фаза 1: MonthView — выбор highlight-события месяца

### Файл: `src/miniapp/src/views/MonthView.vue`

### Текущее состояние
- Отображает список событий, сгруппированных по дням (`groupedEvents`), с inline edit/delete.
- Загружает `events` и `weeklySummaries` (для блокировки редактирования событий).
- Нет никакой связи с monthly summary и highlight.

### Что нужно сделать

**1.1. Добавить данные для highlight:**
- Импортировать `setHighlight` из `'../api/summaries'`.
- Импортировать `isSummaryLocked` из `'../composables/useLockCheck'`.
- Импортировать `useSettingsStore` для `importance_enabled`.
- Добавить реактивные переменные:
  - `summary = ref<Summary | null>(null)` — текущий monthly summary
  - `yearlySummaries = ref<Summary[]>([])` — для проверки блокировки
  - `selectedEventId = ref<string | null>(null)`
  - `isSelecting = ref(false)`
  - `saving = ref(false)`
- Добавить computed:
  - `summaryLock` — `isSummaryLocked('monthly', monthRange.startStr, monthRange.endStr, yearlySummaries.value)`
  - `hasSavedHighlight` — `summary.value !== null && summary.value.highlight_event_id !== null`
  - `hasUnsavedChanges` — `isSelecting && selectedEventId && selectedEventId !== summary?.highlight_event_id`

**1.2. Обновить `fetchData()`:**
- Добавить загрузку `monthly` summaries (limit: 1, from/to = month range) → `summary.value`
- Добавить загрузку `yearly` summaries (year boundaries текущего месяца) → `yearlySummaries.value`
- При загрузке, если `summary.value?.highlight_event_id` — установить `selectedEventId`
- Сбрасывать `isSelecting = false`, `selectedEventId = null` при смене месяца

**1.3. Добавить функции:**
- `selectEvent(eventId)` — как в WeekView
- `enterSelectionMode()` — как в WeekView
- `cancelSelection()` — как в WeekView
- `saveHighlight()` — вызов `setHighlight('monthly', { event_id, period_start, period_end })`

**1.4. Обновить шаблон:**
- **НЕ убирать** существующий список событий с inline edit/delete — он остаётся.
- **Добавить перед списком событий** блок action-hint (аналогично WeekView): «Выберите главное событие месяца».
- В каждой карточке события (`EventCard`) добавить возможность визуального выделения. Варианты:
  - Обернуть `EventCard` в `div` с классами `event-selectable`, `event-selected`, `event-highlight` (аналогично `day-card--*` из WeekView).
  - В режиме `isSelecting` — клик по карточке вызывает `selectEvent(evt.id)` вместо/помимо стандартного поведения.
  - **Если событие уже является highlight** и мы не в режиме выбора — показать визуальное выделение (border + фон).
- **Добавить после списка** блок `action-buttons` (как в WeekView):
  - Если `isSelecting`: «Отмена» + «Сохранить» (disabled пока нет unsaved changes)
  - Если `hasSavedHighlight` и !isSelecting: «Редактировать» (disabled если `summaryLock.locked`, с замком)
  - Иначе: «Выбрать главное событие» (disabled если `summaryLock.locked`, с замком)

**1.5. Стили:**
- Добавить стили для `.event-selectable`, `.event-selected`, `.event-highlight` — аналогично `day-card--*` из WeekView (border, background с `color-mix`).

### Важные моменты
- В режиме выбора (`isSelecting`) inline edit/delete должен быть **отключён** (не показывать кнопки edit/delete в EventCard).
- Блокировка monthly highlight: если есть yearly summary → monthly заблокирован (замок). Это уже реализовано в `isSummaryLocked('monthly', ...)`.
- `period_start` / `period_end` для monthly: первый и последний день месяца (`monthRange.startStr`, `monthRange.endStr`).

---

## Фаза 2: YearView — выбор highlight-события года

### Файл: `src/miniapp/src/views/YearView.vue`

### Текущее состояние
- Отображает bar chart по месяцам и список событий, сгруппированных по месяц → день, с inline edit/delete.
- Загружает `events` и `weeklySummaries`.

### Что нужно сделать

**2.1. Добавить данные для highlight:**
- Импортировать `setHighlight` из `'../api/summaries'`.
- Импортировать `useSettingsStore` для `importance_enabled`.
- Добавить реактивные переменные:
  - `summary = ref<Summary | null>(null)` — текущий yearly summary
  - `selectedEventId = ref<string | null>(null)`
  - `isSelecting = ref(false)`
  - `saving = ref(false)`
- Computed:
  - `hasSavedHighlight`, `hasUnsavedChanges` — аналогично MonthView
  - **Нет `summaryLock`** — yearly — верхний уровень, никогда не блокируется.

**2.2. Обновить `fetchData()`:**
- Добавить загрузку `yearly` summaries (limit: 1, from/to = year range) → `summary.value`
- Pre-select saved highlight при загрузке
- Reset при смене года

**2.3. Добавить функции:**
- `selectEvent(eventId)`, `enterSelectionMode()`, `cancelSelection()`, `saveHighlight()` — аналогично.
- `saveHighlight()` → `setHighlight('yearly', { event_id, period_start: yearRange.startStr, period_end: yearRange.endStr })`

**2.4. Обновить шаблон:**
- Аналогично MonthView: action-hint, визуальное выделение EventCard, action-buttons.
- Bar chart остаётся без изменений.
- В режиме выбора — inline edit/delete отключён.
- Кнопка «Редактировать» **никогда** не заблокирована (yearly = top level).

**2.5. Стили:**
- Те же стили `.event-selectable`, `.event-selected`, `.event-highlight`.

---

## Фаза 3: Тесты

### Файл: `tests/DayTrace.Tests/Integration/HighlightSelectionTests.cs`

Добавить тесты для monthly и yearly highlight (бэкенд уже поддерживает):

- `SetHighlight_Monthly_Success` — создать событие, вызвать `PUT /summaries/monthly/highlight` с границами месяца, проверить 200.
- `SetHighlight_Yearly_Success` — аналогично для yearly.
- `SetHighlight_Monthly_LockedByYearly_Returns422` — создать yearly summary, попытаться monthly highlight → 422.
- `SetHighlight_Yearly_NeverLocked_Success` — yearly всегда доступен (нет блокировки сверху).

---

## Фаза 4: Верификация

1. `dotnet build` — нет ошибок
2. `dotnet test` — все тесты проходят
3. `npm --prefix src/miniapp run build` — без ошибок
4. Playwright-скриншот MonthView и YearView
5. Обновить `CLAUDE.md` — добавить описание MonthView и YearView highlight

---

## Порядок выполнения

Фазы 1 и 2 (MonthView + YearView) можно выполнять **параллельно** (независимые файлы). Фаза 3 (тесты) — параллельно с фронтендом (бэкенд готов). Фаза 4 — последовательно после завершения всего.

---

## Файлы для изменений (только фронтенд + тесты)

| Файл | Действие |
|------|----------|
| `src/miniapp/src/views/MonthView.vue` | Добавить highlight-логику, визуальное выделение, action-buttons |
| `src/miniapp/src/views/YearView.vue` | Добавить highlight-логику, визуальное выделение, action-buttons |
| `tests/DayTrace.Tests/Integration/HighlightSelectionTests.cs` | Добавить 4 теста для monthly/yearly |
| `CLAUDE.md` | Обновить описание MonthView и YearView |

**Бэкенд не изменяется.**
