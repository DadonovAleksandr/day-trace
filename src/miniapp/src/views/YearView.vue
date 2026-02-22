<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, runSummary } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { isEventLocked } from '../composables/useLockCheck'
import EventCard from '../components/EventCard.vue'
import StarPicker from '../components/StarPicker.vue'
import PeriodNav from '../components/PeriodNav.vue'
import SummarySection from '../components/SummarySection.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const weeklySummaries = ref<Summary[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const generating = ref(false)

const yearOffset = ref(0)

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

const summaryStatus = computed(() => {
  if (!summary.value) return 'none'
  return summary.value.status
})

function getEventLock(evt: EventItem) {
  return isEventLocked(evt.local_date, weeklySummaries.value)
}

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

async function fetchData() {
  loading.value = true
  error.value = null
  try {
    const { startStr, endStr } = yearRange.value
    const [eventsRes, summariesRes, weeklyRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('yearly', { from: startStr, to: endStr, limit: 1 }),
      getSummaries('weekly', { from: startStr, to: endStr, limit: 100 }),
    ])
    events.value = eventsRes.items
    summary.value = summariesRes.items[0] ?? null
    weeklySummaries.value = weeklyRes.items
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить данные'
  } finally {
    loading.value = false
  }
}

async function handleGenerate() {
  generating.value = true
  error.value = null
  try {
    await runSummary('yearly', {
      period_start: yearRange.value.startStr,
      period_end: yearRange.value.endStr,
    })
    await fetchData()
  } catch (err: any) {
    const msg = err.response?.data?.error
    if (msg === 'empty_period') {
      error.value = 'В периоде нет событий'
    } else {
      error.value = err.response?.data?.message || 'Не удалось сформировать итог'
    }
  } finally {
    generating.value = false
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
      <SummarySection
        title="Итог года"
        :status="summaryStatus"
        :event-count="summary?.content?.total_events"
        :generating="generating"
        @generate="handleGenerate"
      />

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
        <div v-for="monthGroup in groupedByMonth" :key="monthGroup.monthKey" class="month-group">
          <h3 class="month-header">
            {{ monthGroup.monthLabel }}
            <span class="month-count">{{ monthGroup.eventCount }}</span>
          </h3>

          <div v-for="dayGroup in monthGroup.days" :key="dayGroup.date" class="day-group">
            <h4 class="day-label">{{ dayGroup.dateLabel }}</h4>
            <div v-for="evt in dayGroup.events" :key="evt.id">
              <!-- Edit mode -->
              <div v-if="editingId === evt.id" class="event-form event-form--inline">
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
                  :event="evt"
                  :editable="true"
                  :locked="getEventLock(evt).locked"
                  :lock-reason="getEventLock(evt).reason"
                  @edit="startEdit"
                  @delete="deletingId = $event.id"
                />

                <!-- Delete confirmation -->
                <Transition name="form">
                  <div v-if="deletingId === evt.id" class="delete-confirm">
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

.btn--secondary {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.12));
}

.btn--danger {
  background: var(--dt-error-text, #e53935);
  color: #fff;
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
