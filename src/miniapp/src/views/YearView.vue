<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, setHighlight } from '../api/summaries'
import { useSettingsStore } from '../stores/settings'
import PeriodNav from '../components/PeriodNav.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const settingsStore = useSettingsStore()

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const monthlySummaries = ref<Summary[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const yearOffset = ref(0)
const selectedEventId = ref<string | null>(null)
const isSelecting = ref(false)

const yearRange = computed(() => {
  const now = new Date()
  const year = now.getFullYear() + yearOffset.value
  const start = new Date(year, 0, 1)
  const end = new Date(year, 11, 31)
  return {
    year,
    start,
    end,
    startStr: formatDateISO(start),
    endStr: formatDateISO(end),
  }
})

/** Build 12 month cards with monthly highlight events */
const monthCards = computed(() => {
  const eventsById = new Map<string, EventItem>()
  for (const evt of events.value) {
    eventsById.set(evt.id, evt)
  }

  // Index monthly summaries by month key (YYYY-MM)
  const summaryByMonth = new Map<string, Summary>()
  for (const ms of monthlySummaries.value) {
    if (ms.status === 'generated') {
      const monthKey = ms.period_start.substring(0, 7)
      summaryByMonth.set(monthKey, ms)
    }
  }

  const { year } = yearRange.value
  const cards: Array<{
    key: string
    monthLabel: string
    highlightEvent: EventItem | null
    hasSummary: boolean
  }> = []

  for (let m = 0; m < 12; m++) {
    const monthKey = `${year}-${String(m + 1).padStart(2, '0')}`
    const monthDate = new Date(year, m, 1)
    const monthLabel = monthDate.toLocaleDateString('ru-RU', { month: 'long' })

    const ms = summaryByMonth.get(monthKey)
    const highlightEvent = ms?.highlight_event_id
      ? eventsById.get(ms.highlight_event_id) ?? null
      : null

    cards.push({
      key: monthKey,
      monthLabel,
      highlightEvent,
      hasSummary: !!ms,
    })
  }

  return cards
})

const selectableHighlightIds = computed(() => {
  const ids = new Set<string>()
  for (const card of monthCards.value) {
    if (card.highlightEvent) {
      ids.add(card.highlightEvent.id)
    }
  }
  return ids
})

const selectableHighlightCount = computed(() => selectableHighlightIds.value.size)

const selectionGuard = computed(() => {
  if (selectableHighlightCount.value === 0) {
    return { locked: true, reason: 'Сначала выберите главные события месяцев' }
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
    const result = await setHighlight('yearly', {
      event_id: selectedEventId.value,
      period_start: yearRange.value.startStr,
      period_end: yearRange.value.endStr,
    })
    summary.value = result
    isSelecting.value = false
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось сохранить выбор'
  } finally {
    saving.value = false
  }
}

async function fetchAllEventsInRange(from: string, to: string) {
  const allEvents: EventItem[] = []
  let cursor: string | undefined
  while (true) {
    const page = await getEvents({ from, to, limit: 100, cursor })
    allEvents.push(...page.items)
    if (!page.next_cursor) break
    cursor = page.next_cursor
  }
  return allEvents
}

async function fetchData() {
  loading.value = true
  error.value = null
  isSelecting.value = false
  selectedEventId.value = null
  try {
    const { startStr, endStr } = yearRange.value
    const [yearEvents, monthlyRes, yearlyRes] = await Promise.all([
      fetchAllEventsInRange(startStr, endStr),
      getSummaries('monthly', { from: startStr, to: endStr, limit: 20 }),
      getSummaries('yearly', { from: startStr, to: endStr, limit: 1 }),
    ])
    events.value = yearEvents
    monthlySummaries.value = monthlyRes.items
    summary.value = yearlyRes.items[0] ?? null

    if (summary.value?.highlight_event_id) {
      selectedEventId.value = summary.value.highlight_event_id
    }
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить данные'
  } finally {
    loading.value = false
  }
}

watch(yearOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="year-view">
    <h2 class="view-title">Год</h2>

    <PeriodNav
      :label="String(yearRange.year)"
      :can-go-forward="yearOffset < 0"
      @prev="yearOffset--"
      @next="yearOffset++"
    />

    <ErrorBanner v-if="error" :message="error" @dismiss="error = null" />

    <LoadingSkeleton v-if="loading" :lines="4" />

    <template v-else>
      <!-- Action hint -->
      <div class="action-area">
        <template v-if="isSelecting">
          <p class="action-hint">Выберите главное событие года</p>
        </template>
        <template v-else-if="hasSavedHighlight">
          <p class="action-hint action-hint--saved">Главное событие выбрано</p>
        </template>
        <template v-else>
          <p class="action-hint">
            {{ selectableHighlightCount ? 'Выберите главное событие года' : 'Сначала выберите главные события месяцев' }}
          </p>
        </template>
      </div>

      <!-- Month cards -->
      <div class="month-cards">
        <div
          v-for="card in monthCards"
          :key="card.key"
          class="month-card"
          :class="{
            'month-card--selected': isSelecting && card.highlightEvent && selectedEventId === card.highlightEvent.id,
            'month-card--empty': !card.highlightEvent,
            'month-card--highlight': hasSavedHighlight && card.highlightEvent && summary?.highlight_event_id === card.highlightEvent.id && !isSelecting,
            'month-card--selectable': card.highlightEvent && isSelecting,
          }"
          @click="card.highlightEvent && selectEvent(card.highlightEvent.id)"
        >
          <div class="month-card__header">
            <span class="month-card__label">{{ card.monthLabel }}</span>
          </div>
          <div v-if="card.highlightEvent" class="month-card__body">
            <p class="month-card__text">{{ card.highlightEvent.text }}</p>
            <div v-if="settingsStore.settings?.importance_enabled" class="month-card__importance">
              <span v-for="s in card.highlightEvent.importance" :key="s" class="star">★</span>
            </div>
          </div>
          <div v-else class="month-card__empty-text">Главное событие не выбрано</div>
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
            @click="enterSelectionMode"
          >
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
.year-view {
  max-width: 600px;
  margin: 0 auto;
}

.view-title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  text-align: center;
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

/* Month cards */
.month-cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.month-card {
  background: var(--tg-secondary-bg-color);
  border: 2px solid transparent;
  border-radius: 14px;
  padding: 12px 14px;
  transition: all 200ms ease;
  cursor: default;
}

.month-card--selectable {
  cursor: pointer;
}

.month-card--selectable:active {
  transform: scale(0.98);
}

.month-card--empty {
  opacity: 0.55;
}

.month-card--selected {
  border-color: var(--tg-button-color, #3390ec);
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 6%, var(--tg-secondary-bg-color));
}

.month-card--highlight {
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 8%, var(--tg-secondary-bg-color));
  border-color: color-mix(in srgb, var(--tg-button-color, #3390ec) 30%, transparent);
}

.month-card__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}

.month-card__label {
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
  text-transform: capitalize;
}

.month-card__body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.month-card__text {
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

.month-card__importance {
  display: flex;
  gap: 1px;
}

.star {
  font-size: 13px;
  color: var(--tg-button-color, #3390ec);
  line-height: 1;
}

.month-card__empty-text {
  font-size: 13px;
  color: var(--tg-hint-color);
  font-style: italic;
}

/* Action buttons */
.action-buttons {
  display: flex;
  gap: 8px;
  margin-top: 16px;
  padding-bottom: 16px;
}

.action-buttons .btn {
  flex: 1;
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
  justify-content: center;
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
