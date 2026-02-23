<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, setHighlight } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { isEventLocked } from '../composables/useLockCheck'
import { useSettingsStore } from '../stores/settings'
import EventCard from '../components/EventCard.vue'
import StarPicker from '../components/StarPicker.vue'
import PeriodNav from '../components/PeriodNav.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const weeklySummaries = ref<Summary[]>([])
const monthlySummaries = ref<Summary[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const yearOffset = ref(0)
const selectedEventId = ref<string | null>(null)
const isSelecting = ref(false)
const settingsStore = useSettingsStore()

const {
  editingId,
  editText,
  editImportance,
  deletingId,
  submitting: editSubmitting,
  editError,
  startEdit,
  cancelEdit,
  handleEdit,
  handleDelete,
} = useEventEditing(fetchData)

const editTextCharCount = computed(() => editText.value.length)

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

const groupedByMonth = computed(() => {
  const months: Record<string, EventItem[]> = {}
  for (const evt of events.value) {
    const monthKey = evt.local_date.substring(0, 7)
    if (!months[monthKey]) months[monthKey] = []
    months[monthKey].push(evt)
  }

  const sortedMonths = Object.entries(months).sort(([a], [b]) => a.localeCompare(b))

  return sortedMonths.map(([monthKey, monthEvents]) => {
    const days: Record<string, EventItem[]> = {}
    for (const evt of monthEvents) {
      if (!days[evt.local_date]) days[evt.local_date] = []
      days[evt.local_date]!.push(evt)
    }

    const sortedDays = Object.entries(days).sort(([a], [b]) => a.localeCompare(b))

    const monthDate = new Date(monthKey + '-01T00:00:00')
    return {
      monthKey,
      monthLabel: monthDate.toLocaleDateString('ru-RU', { month: 'long' }),
      eventCount: monthEvents.length,
      days: sortedDays.map(([date, dayEvents]) => ({
        date,
        dateLabel: new Date(date + 'T00:00:00').toLocaleDateString('ru-RU', {
          weekday: 'short',
          day: 'numeric',
          month: 'short',
        }),
        events: dayEvents.sort((a, b) => b.importance - a.importance),
      })),
    }
  })
})

const monthEventCounts = computed(() => {
  const counts = new Array(12).fill(0)
  for (const evt of events.value) {
    const month = parseInt(evt.local_date.substring(5, 7), 10) - 1
    if (month >= 0 && month < 12) {
      counts[month]++
    }
  }
  return counts
})

const maxMonthCount = computed(() => Math.max(...monthEventCounts.value, 1))

const monthNames = ['Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн', 'Июл', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек']

const hasSavedHighlight = computed(() => {
  return summary.value !== null && summary.value.highlight_event_id !== null
})

const selectableHighlightIds = computed(() => {
  const yearEventIds = new Set(events.value.map((evt) => evt.id))
  const ids = new Set<string>()

  for (const s of monthlySummaries.value) {
    if (s.status !== 'generated' || !s.highlight_event_id) continue
    if (!yearEventIds.has(s.highlight_event_id)) continue
    ids.add(s.highlight_event_id)
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

const hasUnsavedChanges = computed(() => {
  if (!isSelecting.value) return false
  if (!selectedEventId.value) return false
  return selectedEventId.value !== summary.value?.highlight_event_id
})

function getEventLock(evt: EventItem) {
  return isEventLocked(evt.local_date, weeklySummaries.value)
}

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

function canSelectHighlight(eventId: string) {
  return selectableHighlightIds.value.has(eventId)
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

function selectEvent(eventId: string) {
  if (!isSelecting.value) return
  selectedEventId.value = selectedEventId.value === eventId ? null : eventId
}

function enterSelectionMode() {
  if (selectionGuard.value.locked) return
  cancelEdit()
  deletingId.value = null
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

async function fetchData() {
  loading.value = true
  error.value = null
  isSelecting.value = false
  selectedEventId.value = null
  deletingId.value = null
  cancelEdit()
  try {
    const { startStr, endStr } = yearRange.value
    const weeklyRangeStart = new Date(yearRange.value.start)
    weeklyRangeStart.setDate(weeklyRangeStart.getDate() - 6)
    const weeklyRangeEnd = new Date(yearRange.value.end)
    weeklyRangeEnd.setDate(weeklyRangeEnd.getDate() + 6)

    const [yearEvents, weeklyRes, monthlyRes, yearlyRes] = await Promise.all([
      fetchAllEventsInRange(startStr, endStr),
      getSummaries('weekly', {
        from: formatDateISO(weeklyRangeStart),
        to: formatDateISO(weeklyRangeEnd),
        limit: 100,
      }),
      getSummaries('monthly', { from: startStr, to: endStr, limit: 20 }),
      getSummaries('yearly', { from: startStr, to: endStr, limit: 1 }),
    ])
    events.value = yearEvents
    weeklySummaries.value = weeklyRes.items
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

    <ErrorBanner v-if="error || editError" :message="error || editError || ''" @dismiss="error = null; editError && (editError = null)" />

    <LoadingSkeleton v-if="loading" :lines="4" />

    <template v-else>
      <!-- Month chart -->
      <div v-if="events.length" class="month-chart">
        <h3 class="month-chart__title">Событий по месяцам</h3>
        <div class="chart-bars">
          <div v-for="(count, idx) in monthEventCounts" :key="idx" class="chart-col">
            <div class="chart-bar-wrap">
              <div
                class="chart-bar"
                :style="{ height: (count / maxMonthCount * 100) + '%' }"
              ></div>
            </div>
            <span class="chart-label">{{ monthNames[idx] }}</span>
            <span v-if="count > 0" class="chart-count">{{ count }}</span>
          </div>
        </div>
      </div>

      <!-- Events grouped by month then day -->
      <div v-if="groupedByMonth.length" class="month-groups">
        <div class="action-area">
          <template v-if="isSelecting">
            <p class="action-hint">
              {{ selectableHighlightCount ? 'Выберите главное событие года (из главных событий месяцев)' : 'Нет главных событий месяцев для выбора' }}
            </p>
          </template>
          <template v-else-if="hasSavedHighlight">
            <p class="action-hint action-hint--saved">Главное событие выбрано</p>
          </template>
          <template v-else>
            <p class="action-hint">
              {{ selectableHighlightCount ? 'Выберите главное событие года из главных событий месяцев' : 'Сначала выберите главные события месяцев' }}
            </p>
          </template>
        </div>

        <div v-for="monthGroup in groupedByMonth" :key="monthGroup.monthKey" class="month-group">
          <h3 class="month-header">
            {{ monthGroup.monthLabel }}
            <span class="month-count">{{ monthGroup.eventCount }}</span>
          </h3>

          <div v-for="dayGroup in monthGroup.days" :key="dayGroup.date" class="day-group">
            <h4 class="day-label">{{ dayGroup.dateLabel }}</h4>
            <div v-for="evt in dayGroup.events" :key="evt.id">
              <!-- Edit mode -->
              <div v-if="editingId === evt.id && !isSelecting" class="event-form event-form--inline">
                <div class="form-field">
                  <textarea v-model="editText" maxlength="500" rows="2" class="form-textarea"></textarea>
                  <span class="char-count" :class="{ 'char-count--warn': editTextCharCount > 450 }">
                    {{ editTextCharCount }}/500
                  </span>
                </div>
                <div class="form-field">
                  <StarPicker v-model="editImportance" />
                </div>
                <div class="form-actions">
                  <button class="btn btn--secondary" @click="cancelEdit">Отмена</button>
                  <button
                    class="btn btn--primary"
                    :disabled="!editText.trim() || editText.length > 500 || editSubmitting"
                    @click="handleEdit(evt.id)"
                  >
                    {{ editSubmitting ? 'Сохраняем...' : 'Сохранить' }}
                  </button>
                </div>
              </div>

              <!-- Display mode -->
              <template v-else>
                <EventCard
                  :class="{
                    'event-card--selected': isSelecting && selectedEventId === evt.id,
                    'event-card--highlight': hasSavedHighlight && summary?.highlight_event_id === evt.id && !isSelecting,
                    'event-card--selectable': isSelecting && canSelectHighlight(evt.id),
                    'event-card--selection-disabled': isSelecting && !canSelectHighlight(evt.id),
                  }"
                  :event="evt"
                  :editable="!isSelecting"
                  :locked="getEventLock(evt).locked"
                  :lock-reason="getEventLock(evt).reason"
                  :show-importance="settingsStore.settings?.importance_enabled"
                  @click="isSelecting && canSelectHighlight(evt.id) && selectEvent(evt.id)"
                  @edit="startEdit"
                  @delete="deletingId = $event.id"
                />

                <!-- Delete confirmation -->
                <Transition name="form">
                  <div v-if="deletingId === evt.id && !isSelecting" class="delete-confirm">
                    <p>Удалить событие?</p>
                    <div class="form-actions">
                      <button class="btn btn--secondary" @click="deletingId = null">Нет</button>
                      <button class="btn btn--danger" :disabled="editSubmitting" @click="handleDelete(evt.id)">
                        {{ editSubmitting ? '...' : 'Да, удалить' }}
                      </button>
                    </div>
                  </div>
                </Transition>
              </template>
            </div>
          </div>
        </div>

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
      </div>

      <EmptyState v-else message="Нет событий за этот год" icon="year" />
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

/* Chart */
.month-chart {
  background: var(--tg-secondary-bg-color);
  border-radius: 14px;
  padding: 14px 16px;
  margin: 14px 0;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
}

.month-chart__title {
  margin: 0 0 12px;
  font-size: 14px;
  font-weight: 600;
}

.chart-bars {
  display: flex;
  gap: 4px;
  align-items: flex-end;
  height: 80px;
}

.chart-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

.chart-bar-wrap {
  width: 100%;
  height: 60px;
  display: flex;
  align-items: flex-end;
  justify-content: center;
}

.chart-bar {
  width: 70%;
  min-height: 2px;
  background: var(--tg-button-color, #3390ec);
  border-radius: 3px 3px 0 0;
  transition: height 0.4s ease;
}

.chart-label {
  font-size: 9px;
  color: var(--tg-hint-color);
}

.chart-count {
  font-size: 9px;
  color: var(--tg-text-color);
  font-weight: 600;
}

.action-area {
  margin: 0 0 2px;
  text-align: center;
}

.action-hint {
  margin: 0;
  font-size: 13px;
  color: var(--tg-hint-color);
}

.action-hint--saved {
  color: var(--tg-button-color, #3390ec);
  font-weight: 500;
}

/* Month groups */
.month-groups {
  display: flex;
  flex-direction: column;
  gap: 18px;
  margin-top: 14px;
}

.month-header {
  font-size: 15px;
  margin: 0;
  text-transform: capitalize;
  display: flex;
  align-items: center;
  gap: 8px;
}

.month-count {
  background: var(--tg-button-color, #3390ec);
  color: var(--tg-button-text-color, #fff);
  font-size: 11px;
  padding: 1px 7px;
  border-radius: 10px;
  font-weight: 600;
}

.day-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-left: 8px;
  margin-top: 6px;
}

.day-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
  margin: 0;
  text-transform: capitalize;
  letter-spacing: 0.02em;
}

:deep(.event-card.event-card--selectable) {
  cursor: pointer;
  transition: all 200ms ease;
}

:deep(.event-card.event-card--selectable:active) {
  transform: scale(0.98);
}

:deep(.event-card.event-card--selection-disabled) {
  opacity: 0.55;
}

:deep(.event-card.event-card--selected) {
  border-color: var(--tg-button-color, #3390ec);
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 6%, var(--tg-secondary-bg-color));
}

:deep(.event-card.event-card--highlight) {
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 8%, var(--tg-secondary-bg-color));
  border-color: color-mix(in srgb, var(--tg-button-color, #3390ec) 30%, transparent);
}

/* Form */
.event-form {
  background: var(--tg-secondary-bg-color);
  border-radius: 14px;
  padding: 14px;
  margin: 12px 0;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
}

.event-form--inline {
  margin: 0 0 8px;
}

.form-field {
  margin-bottom: 12px;
  position: relative;
}

.form-textarea {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.1));
  border-radius: 10px;
  font-size: 14px;
  background: var(--tg-bg-color);
  color: var(--tg-text-color);
  resize: none;
  transition: border-color 200ms ease;
  font-family: inherit;
}

.form-textarea:focus {
  outline: none;
  border-color: var(--tg-button-color, #2481cc);
}

.char-count {
  position: absolute;
  right: 10px;
  bottom: 6px;
  font-size: 11px;
  color: var(--tg-hint-color);
}

.char-count--warn {
  color: var(--dt-error-text, #e53935);
}

.form-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  margin-top: 8px;
}

.btn {
  padding: 8px 18px;
  border: none;
  border-radius: 9px;
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
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.12));
}

.btn--secondary:disabled {
  opacity: 0.45;
  cursor: default;
}

.btn--secondary:disabled:active {
  transform: none;
}

.btn--danger {
  background: var(--dt-error-text, #e53935);
  color: #fff;
}

.action-buttons {
  display: flex;
  gap: 8px;
  justify-content: center;
  margin-top: -2px;
  padding-bottom: 16px;
}

/* Delete confirmation */
.delete-confirm {
  width: 100%;
  background: var(--dt-warning-bg, rgba(255,152,0,0.08));
  border: 1px solid var(--dt-warning-border, rgba(255,152,0,0.16));
  border-radius: 10px;
  padding: 10px 12px;
  margin-top: 4px;
}

.delete-confirm p {
  margin-bottom: 8px;
  font-size: 13px;
  font-weight: 500;
}

/* Form transition */
.form-enter-active,
.form-leave-active {
  transition: all 0.2s ease;
}

.form-enter-from,
.form-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}
</style>
