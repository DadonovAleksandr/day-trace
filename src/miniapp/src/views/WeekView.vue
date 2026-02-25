<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, setHighlight } from '../api/summaries'
import { isSummaryLocked } from '../composables/useLockCheck'
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

const weekOffset = ref(0)

/** Currently selected event ID in selection mode */
const selectedEventId = ref<string | null>(null)

/** Whether user is in selection/editing mode */
const isSelecting = ref(false)

const weekRange = computed(() => {
  const now = new Date()
  const day = now.getDay()
  const start = new Date(now)
  start.setDate(now.getDate() - ((day === 0 ? 7 : day) - 1) + weekOffset.value * 7)
  start.setHours(0, 0, 0, 0)

  const end = new Date(start)
  end.setDate(start.getDate() + 6)

  return {
    start,
    end,
    startStr: formatDateISO(start),
    endStr: formatDateISO(end),
  }
})

const weekLabel = computed(() => {
  const { start, end } = weekRange.value
  const opts: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' }
  return `${start.toLocaleDateString('ru-RU', opts)} — ${end.toLocaleDateString('ru-RU', opts)}`
})

/** Build array of all 7 days for the current week */
const weekDays = computed(() => {
  const { start } = weekRange.value
  const eventsByDate = new Map<string, EventItem>()
  for (const evt of events.value) {
    // One event per day by design; take the first if multiple exist
    if (!eventsByDate.has(evt.local_date)) {
      eventsByDate.set(evt.local_date, evt)
    }
  }

  const days: Array<{
    dateStr: string
    weekdayLabel: string
    dateLabel: string
    event: EventItem | null
  }> = []

  for (let i = 0; i < 7; i++) {
    const d = new Date(start)
    d.setDate(start.getDate() + i)
    const dateStr = formatDateISO(d)
    const weekdayLabel = d.toLocaleDateString('ru-RU', { weekday: 'short' })
    const dateLabel = d.toLocaleDateString('ru-RU', { day: 'numeric', month: 'short' })

    days.push({
      dateStr,
      weekdayLabel,
      dateLabel,
      event: eventsByDate.get(dateStr) ?? null,
    })
  }

  return days
})

const hasAnyEvents = computed(() => events.value.length > 0)

const summaryLock = computed(() => {
  return isSummaryLocked('weekly', weekRange.value.startStr, weekRange.value.endStr, monthlySummaries.value)
})

/** Whether the highlight has been saved (summary exists with a highlight) */
const hasSavedHighlight = computed(() => {
  return summary.value !== null && summary.value.highlight_event_id !== null
})

/** Whether user changed the selection compared to saved state */
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
    const result = await setHighlight('weekly', {
      event_id: selectedEventId.value,
      period_start: weekRange.value.startStr,
      period_end: weekRange.value.endStr,
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
    const { startStr, endStr } = weekRange.value

    const monthStart = new Date(weekRange.value.start.getFullYear(), weekRange.value.start.getMonth(), 1)
    const monthEnd = new Date(monthStart.getFullYear(), monthStart.getMonth() + 1, 0)

    const [eventsRes, summariesRes, monthlyRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('weekly', { from: startStr, to: endStr, limit: 1 }),
      getSummaries('monthly', {
        from: formatDateISO(monthStart),
        to: formatDateISO(monthEnd),
        limit: 10,
      }),
    ])
    events.value = eventsRes.items
    summary.value = summariesRes.items[0] ?? null
    monthlySummaries.value = monthlyRes.items

    // Pre-select saved highlight
    if (summary.value?.highlight_event_id) {
      selectedEventId.value = summary.value.highlight_event_id
    }
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить данные'
  } finally {
    loading.value = false
  }
}

watch(weekOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="week-view">
    <h2 class="view-title">Неделя</h2>

    <PeriodNav
      :label="weekLabel"
      :can-go-forward="weekOffset < 0"
      @prev="weekOffset--"
      @next="weekOffset++"
    />

    <ErrorBanner v-if="error" :message="error" @dismiss="error = null" />

    <LoadingSkeleton v-if="loading" :lines="4" />

    <template v-else>
      <template v-if="hasAnyEvents">
        <!-- Action hint -->
        <div class="action-area">
          <template v-if="isSelecting">
            <p class="action-hint">Выберите главное событие недели</p>
          </template>
          <template v-else-if="hasSavedHighlight">
            <p class="action-hint action-hint--saved">Главное событие выбрано</p>
          </template>
          <template v-else>
            <p class="action-hint">Выберите главное событие недели</p>
          </template>
        </div>

        <!-- Day cards grid -->
        <div class="week-days">
          <div
            v-for="day in weekDays"
            :key="day.dateStr"
            class="day-card"
            :class="{
              'day-card--selected': isSelecting && selectedEventId === day.event?.id,
              'day-card--empty': !day.event,
              'day-card--highlight': hasSavedHighlight && summary?.highlight_event_id === day.event?.id && !isSelecting,
              'day-card--selectable': day.event && isSelecting,
            }"
            @click="day.event && selectEvent(day.event.id)"
          >
            <div class="day-card__header">
              <span class="day-card__weekday">{{ day.weekdayLabel }}</span>
              <span class="day-card__date">{{ day.dateLabel }}</span>
            </div>
            <div v-if="day.event" class="day-card__body">
              <p class="day-card__text">{{ day.event.text }}</p>
              <div v-if="settingsStore.settings?.importance_enabled" class="day-card__importance">
                <span v-for="s in day.event.importance" :key="s" class="star">★</span>
              </div>
            </div>
            <div v-else class="day-card__empty-text">Нет записи</div>
          </div>
        </div>

        <!-- Action buttons -->
        <div class="action-buttons">
          <template v-if="isSelecting">
            <button
              class="btn btn--secondary"
              @click="cancelSelection"
            >
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
              :disabled="summaryLock.locked"
              :title="summaryLock.locked ? summaryLock.reason : ''"
              @click="enterSelectionMode"
            >
              <span v-if="summaryLock.locked" class="btn__lock">&#x1F512;</span>
              Редактировать
            </button>
          </template>
          <template v-else>
            <!-- No highlight saved yet, auto-enter selection mode -->
            <button
              v-if="!isSelecting"
              class="btn btn--primary"
              :disabled="summaryLock.locked"
              :title="summaryLock.locked ? summaryLock.reason : ''"
              @click="enterSelectionMode"
            >
              <span v-if="summaryLock.locked" class="btn__lock">&#x1F512;</span>
              Выбрать главное событие
            </button>
          </template>
        </div>
      </template>

      <EmptyState v-else message="Нет событий за эту неделю" icon="week" />
    </template>
  </div>
</template>

<style scoped>
.week-view {
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

/* Day cards */
.week-days {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.day-card {
  background: var(--tg-secondary-bg-color);
  border: 2px solid transparent;
  border-radius: 14px;
  padding: 12px 14px;
  transition: all 200ms ease;
  cursor: default;
}

.day-card--selectable {
  cursor: pointer;
}

.day-card--selectable:active {
  transform: scale(0.98);
}

.day-card--empty {
  opacity: 0.55;
}

.day-card--selected {
  border-color: var(--tg-button-color, #3390ec);
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 6%, var(--tg-secondary-bg-color));
}

.day-card--highlight {
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 8%, var(--tg-secondary-bg-color));
  border-color: color-mix(in srgb, var(--tg-button-color, #3390ec) 30%, transparent);
}

.day-card__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}

.day-card__weekday {
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
  text-transform: capitalize;
}

.day-card__date {
  font-size: 12px;
  color: var(--tg-hint-color);
}

.day-card__body {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.day-card__text {
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

.day-card__importance {
  display: flex;
  gap: 1px;
}

.star {
  font-size: 13px;
  color: var(--tg-button-color, #3390ec);
  line-height: 1;
}

.day-card__empty-text {
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
