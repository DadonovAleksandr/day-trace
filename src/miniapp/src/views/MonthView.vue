<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, setHighlight } from '../api/summaries'
import { isSummaryLocked } from '../composables/useLockCheck'
import { useSettingsStore } from '../stores/settings'
import PeriodNav from '../components/PeriodNav.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const settingsStore = useSettingsStore()

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const weeklySummaries = ref<Summary[]>([])
const yearlySummaries = ref<Summary[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const monthOffset = ref(0)
const selectedEventId = ref<string | null>(null)
const isSelecting = ref(false)

const monthRange = computed(() => {
  const now = new Date()
  const year = now.getFullYear()
  const month = now.getMonth() + monthOffset.value

  const start = new Date(year, month, 1)
  const end = new Date(start.getFullYear(), start.getMonth() + 1, 0)

  return {
    start,
    end,
    startStr: formatDateISO(start),
    endStr: formatDateISO(end),
  }
})

const monthLabel = computed(() => {
  const { start } = monthRange.value
  return start.toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' })
})

/** Build week cards for all weeks in the month (Mon–Sun), matched against weekly summaries */
const weekCards = computed(() => {
  const { start, end } = monthRange.value
  const eventsById = new Map<string, EventItem>()
  for (const evt of events.value) {
    eventsById.set(evt.id, evt)
  }

  // Index weekly summaries by period_start for quick lookup
  const summaryByStart = new Map<string, Summary>()
  for (const ws of weeklySummaries.value) {
    if (ws.status === 'generated') {
      summaryByStart.set(ws.period_start, ws)
    }
  }

  // Find the Monday of the week containing the 1st of the month
  const firstDay = new Date(start)
  const dow = firstDay.getDay() // 0=Sun, 1=Mon, ...
  const mondayOffset = dow === 0 ? -6 : 1 - dow
  const weekStart = new Date(firstDay)
  weekStart.setDate(firstDay.getDate() + mondayOffset)

  const cards: Array<{
    key: string
    weekLabel: string
    periodStart: string
    periodEnd: string
    highlightEvent: EventItem | null
    hasSummary: boolean
  }> = []

  // Generate all weeks until we pass the end of the month
  const cursor = new Date(weekStart)
  while (cursor <= end) {
    const wkStart = new Date(cursor)
    const wkEnd = new Date(cursor)
    wkEnd.setDate(wkStart.getDate() + 6)

    const periodStart = formatDateISO(wkStart)
    const periodEnd = formatDateISO(wkEnd)

    const startLabel = wkStart.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' })
    const endLabel = wkEnd.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' })

    const ws = summaryByStart.get(periodStart)
    const highlightEvent = ws?.highlight_event_id
      ? eventsById.get(ws.highlight_event_id) ?? null
      : null

    cards.push({
      key: periodStart,
      weekLabel: `${startLabel} — ${endLabel}`,
      periodStart,
      periodEnd,
      highlightEvent,
      hasSummary: !!ws,
    })

    // Move to next Monday
    cursor.setDate(cursor.getDate() + 7)
  }

  return cards
})

const selectableHighlightIds = computed(() => {
  const ids = new Set<string>()
  for (const card of weekCards.value) {
    if (card.highlightEvent) {
      ids.add(card.highlightEvent.id)
    }
  }
  return ids
})

const selectableHighlightCount = computed(() => selectableHighlightIds.value.size)

const summaryLock = computed(() => {
  return isSummaryLocked('monthly', monthRange.value.startStr, monthRange.value.endStr, yearlySummaries.value)
})

const selectionGuard = computed(() => {
  if (summaryLock.value.locked) return summaryLock.value
  if (selectableHighlightCount.value === 0) {
    return { locked: true, reason: 'Сначала выберите главные события недель' }
  }
  return { locked: false, reason: '' }
})

const hasSavedHighlight = computed(() => {
  return summary.value !== null && summary.value.highlight_event_id !== null
})

const hasUnsavedChanges = computed(() => {
  if (!isSelecting.value) return false
  if (!selectedEventId.value) return false
  return selectedEventId.value !== summary.value?.highlight_event_id
})

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

function selectEvent(eventId: string) {
  if (!isSelecting.value) return
  selectedEventId.value = selectedEventId.value === eventId ? null : eventId
}

function enterSelectionMode() {
  if (selectionGuard.value.locked) return
  isSelecting.value = true
  selectedEventId.value = summary.value?.highlight_event_id ?? null
}

function cancelSelection() {
  isSelecting.value = false
  selectedEventId.value = summary.value?.highlight_event_id ?? null
}

async function saveHighlight() {
  if (!selectedEventId.value) return
  saving.value = true
  error.value = null
  try {
    const result = await setHighlight('monthly', {
      event_id: selectedEventId.value,
      period_start: monthRange.value.startStr,
      period_end: monthRange.value.endStr,
    })
    summary.value = result
    isSelecting.value = false
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось сохранить выбор'
  } finally {
    saving.value = false
  }
}

async function fetchData() {
  loading.value = true
  error.value = null
  isSelecting.value = false
  selectedEventId.value = null
  try {
    const { startStr, endStr } = monthRange.value
    const yearStart = new Date(monthRange.value.start.getFullYear(), 0, 1)
    const yearEnd = new Date(monthRange.value.start.getFullYear(), 11, 31)
    const weeklyRangeStart = new Date(monthRange.value.start)
    weeklyRangeStart.setDate(weeklyRangeStart.getDate() - 6)
    const weeklyRangeEnd = new Date(monthRange.value.end)
    weeklyRangeEnd.setDate(weeklyRangeEnd.getDate() + 6)

    const [eventsRes, weeklyRes, monthlyRes, yearlyRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('weekly', {
        from: formatDateISO(weeklyRangeStart),
        to: formatDateISO(weeklyRangeEnd),
        limit: 20,
      }),
      getSummaries('monthly', { from: startStr, to: endStr, limit: 1 }),
      getSummaries('yearly', {
        from: formatDateISO(yearStart),
        to: formatDateISO(yearEnd),
        limit: 5,
      }),
    ])
    events.value = eventsRes.items
    weeklySummaries.value = weeklyRes.items
    summary.value = monthlyRes.items[0] ?? null
    yearlySummaries.value = yearlyRes.items

    if (summary.value?.highlight_event_id) {
      selectedEventId.value = summary.value.highlight_event_id
    }
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить данные'
  } finally {
    loading.value = false
  }
}

watch(monthOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="month-view">
    <h2 class="view-title">Месяц</h2>

    <PeriodNav
      :label="monthLabel"
      :can-go-forward="monthOffset < 0"
      @prev="monthOffset--"
      @next="monthOffset++"
    />

    <ErrorBanner v-if="error" :message="error" @dismiss="error = null" />

    <LoadingSkeleton v-if="loading" :lines="4" />

    <template v-else>
      <!-- Action hint -->
      <div class="action-area">
        <template v-if="isSelecting">
          <p class="action-hint">Выберите главное событие месяца</p>
        </template>
        <template v-else-if="hasSavedHighlight">
          <p class="action-hint action-hint--saved">Главное событие выбрано</p>
        </template>
        <template v-else>
          <p class="action-hint">
            {{ selectableHighlightCount ? 'Выберите главное событие месяца' : 'Сначала выберите главные события недель' }}
          </p>
        </template>
      </div>

      <!-- Week cards -->
      <div class="week-cards">
        <div
          v-for="card in weekCards"
          :key="card.key"
          class="week-card"
          :class="{
            'week-card--selected': isSelecting && card.highlightEvent && selectedEventId === card.highlightEvent.id,
            'week-card--empty': !card.highlightEvent,
            'week-card--highlight': hasSavedHighlight && card.highlightEvent && summary?.highlight_event_id === card.highlightEvent.id && !isSelecting,
            'week-card--selectable': card.highlightEvent && isSelecting,
          }"
          @click="card.highlightEvent && selectEvent(card.highlightEvent.id)"
        >
          <div class="week-card__header">
            <span class="week-card__label">{{ card.weekLabel }}</span>
          </div>
          <div v-if="card.highlightEvent" class="week-card__body">
            <p class="week-card__text">{{ card.highlightEvent.text }}</p>
            <div v-if="settingsStore.settings?.importance_enabled" class="week-card__importance">
              <span v-for="s in card.highlightEvent.importance" :key="s" class="star">★</span>
            </div>
          </div>
          <div v-else class="week-card__empty-text">Главное событие не выбрано</div>
        </div>
      </div>

      <!-- Action buttons -->
      <div class="action-buttons">
        <template v-if="isSelecting">
          <button class="btn btn--secondary" @click="cancelSelection">
            Отмена
          </button>
          <button
            class="btn btn--primary"
            :disabled="!selectedEventId || !hasUnsavedChanges || saving"
            @click="saveHighlight"
          >
            {{ saving ? 'Сохраняем...' : 'Сохранить' }}
          </button>
        </template>
        <template v-else-if="hasSavedHighlight">
          <button
            class="btn btn--secondary"
            :disabled="selectionGuard.locked"
            :title="selectionGuard.locked ? selectionGuard.reason : ''"
            @click="enterSelectionMode"
          >
            <span v-if="selectionGuard.locked" class="btn__lock">&#x1F512;</span>
            Редактировать
          </button>
        </template>
        <template v-else>
          <button
            class="btn btn--primary"
            :disabled="selectionGuard.locked"
            :title="selectionGuard.locked ? selectionGuard.reason : ''"
            @click="enterSelectionMode"
          >
            <span v-if="selectionGuard.locked" class="btn__lock">&#x1F512;</span>
            Выбрать главное событие
          </button>
        </template>
      </div>
    </template>
  </div>
</template>

<style scoped>
.month-view {
  max-width: 600px;
  margin: 0 auto;
}

.view-title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  text-align: center;
  text-transform: capitalize;
}

/* Action area */
.action-area {
  margin: 14px 0 10px;
  text-align: center;
}

.action-hint {
  font-size: 13px;
  color: var(--tg-hint-color);
  margin: 0;
}

.action-hint--saved {
  color: var(--tg-button-color, #3390ec);
  font-weight: 500;
}

/* Week cards */
.week-cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.week-card {
  background: var(--tg-secondary-bg-color);
  border: 2px solid transparent;
  border-radius: 14px;
  padding: 12px 14px;
  transition: all 200ms ease;
  cursor: default;
}

.week-card--selectable {
  cursor: pointer;
}

.week-card--selectable:active {
  transform: scale(0.98);
}

.week-card--empty {
  opacity: 0.55;
}

.week-card--selected {
  border-color: var(--tg-button-color, #3390ec);
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 6%, var(--tg-secondary-bg-color));
}

.week-card--highlight {
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 8%, var(--tg-secondary-bg-color));
  border-color: color-mix(in srgb, var(--tg-button-color, #3390ec) 30%, transparent);
}

.week-card__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}

.week-card__label {
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
  text-transform: capitalize;
}

.week-card__body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.week-card__text {
  margin: 0;
  font-size: 14px;
  line-height: 1.4;
  color: var(--tg-text-color);
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
  text-overflow: ellipsis;
}

.week-card__importance {
  display: flex;
  gap: 1px;
}

.star {
  font-size: 13px;
  color: var(--tg-button-color, #3390ec);
  line-height: 1;
}

.week-card__empty-text {
  font-size: 13px;
  color: var(--tg-hint-color);
  font-style: italic;
}

/* Action buttons */
.action-buttons {
  display: flex;
  gap: 8px;
  justify-content: center;
  margin-top: 16px;
  padding-bottom: 16px;
}

.btn {
  padding: 10px 22px;
  border: none;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 200ms ease;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.btn:active {
  transform: scale(0.97);
}

.btn--primary {
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.btn--primary:disabled {
  opacity: 0.45;
  cursor: default;
}

.btn--primary:disabled:active {
  transform: none;
}

.btn--secondary {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.12));
}

.btn--secondary:disabled {
  opacity: 0.45;
  cursor: default;
}

.btn--secondary:disabled:active {
  transform: none;
}

.btn__lock {
  font-size: 14px;
  line-height: 1;
}
</style>
