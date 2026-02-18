<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, runSummary } from '../api/summaries'

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const generating = ref(false)

// Year navigation offset (0 = current, -1 = previous, etc.)
const yearOffset = ref(0)

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

const yearLabel = computed(() => {
  return String(yearRange.value.year)
})

// Group events by month then by day
const groupedByMonth = computed(() => {
  const months: Record<string, EventItem[]> = {}
  for (const evt of events.value) {
    // Extract YYYY-MM from local_date
    const monthKey = evt.local_date.substring(0, 7)
    if (!months[monthKey]) months[monthKey] = []
    months[monthKey].push(evt)
  }

  const sortedMonths = Object.entries(months).sort(([a], [b]) => a.localeCompare(b))

  return sortedMonths.map(([monthKey, monthEvents]) => {
    // Group by day within month
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

// Event count per month for visualization (12 months)
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

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

function starsDisplay(n: number): string {
  return '★'.repeat(n) + '☆'.repeat(5 - n)
}

async function fetchData() {
  loading.value = true
  error.value = null
  try {
    const { startStr, endStr } = yearRange.value
    const [eventsRes, summariesRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('yearly', { from: startStr, to: endStr, limit: 1 }),
    ])
    events.value = eventsRes.items
    summary.value = summariesRes.items[0] ?? null
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

function prevYear() {
  yearOffset.value--
}

function nextYear() {
  yearOffset.value++
}

watch(yearOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="year-view">
    <div class="header">
      <h2>📊 Год</h2>
    </div>

    <!-- Year navigation -->
    <div class="year-nav">
      <button class="nav-btn" @click="prevYear">◀</button>
      <span class="year-label">{{ yearLabel }}</span>
      <button class="nav-btn" @click="nextYear" :disabled="yearOffset >= 0">▶</button>
    </div>

    <!-- Error -->
    <div v-if="error" class="error-banner" @click="error = null">
      ❌ {{ error }}
    </div>

    <!-- Loading -->
    <div v-if="loading" class="loading">Загрузка...</div>

    <template v-else>
      <!-- Summary section -->
      <div class="summary-section">
        <h3>Итог года</h3>

        <div v-if="summaryStatus === 'generated'" class="summary-content">
          <p class="summary-meta">✅ {{ summary!.content?.total_events || 0 }} событий</p>
        </div>
        <div v-else-if="summaryStatus === 'generating'" class="summary-status">
          ⏳ Формируется...
        </div>
        <div v-else-if="summaryStatus === 'failed'" class="summary-status error">
          ❌ Ошибка генерации
        </div>
        <div v-else class="summary-status">
          Нет итога
        </div>

        <button
          class="btn-primary generate-btn"
          :disabled="generating"
          @click="handleGenerate"
        >
          {{ generating ? '⏳ Формируем...' : '🔄 Сформировать итог' }}
        </button>
      </div>

      <!-- Month event count visualization -->
      <div v-if="events.length" class="month-chart">
        <h3>Событий по месяцам</h3>
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
            <div v-for="evt in dayGroup.events" :key="evt.id" class="event-card">
              <div class="event-text">{{ evt.text }}</div>
              <span class="importance">{{ starsDisplay(evt.importance) }}</span>
            </div>
          </div>
        </div>
      </div>

      <div v-else class="empty">
        Нет событий за этот год
      </div>
    </template>
  </div>
</template>

<style scoped>
.year-view {
  max-width: 600px;
  margin: 0 auto;
}

.header h2 {
  margin: 0;
}

.year-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
  margin: 12px 0;
}

.nav-btn {
  background: var(--tg-secondary-bg-color);
  border: 1px solid var(--tg-hint-color);
  border-radius: 8px;
  padding: 6px 12px;
  font-size: 16px;
  cursor: pointer;
  color: var(--tg-text-color);
}

.nav-btn:disabled {
  opacity: 0.3;
}

.year-label {
  font-size: 18px;
  font-weight: 700;
}

.error-banner {
  background: #fee;
  border: 1px solid #fcc;
  border-radius: 8px;
  padding: 8px 12px;
  margin: 8px 0;
  font-size: 13px;
  cursor: pointer;
}

.loading,
.empty {
  text-align: center;
  padding: 32px 0;
  color: var(--tg-hint-color);
}

.summary-section {
  background: var(--tg-secondary-bg-color);
  border-radius: 12px;
  padding: 12px;
  margin: 12px 0;
}

.summary-section h3 {
  margin: 0 0 8px;
  font-size: 15px;
}

.summary-content {
  margin-bottom: 8px;
}

.summary-meta {
  font-size: 13px;
  color: var(--tg-hint-color);
}

.summary-status {
  font-size: 13px;
  color: var(--tg-hint-color);
  margin-bottom: 8px;
}

.summary-status.error {
  color: #e53935;
}

.generate-btn {
  width: 100%;
  padding: 10px;
  border: none;
  border-radius: 8px;
  font-size: 14px;
  cursor: pointer;
}

.btn-primary {
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.generate-btn:disabled {
  opacity: 0.5;
}

/* Month event count chart */
.month-chart {
  background: var(--tg-secondary-bg-color);
  border-radius: 12px;
  padding: 12px;
  margin: 12px 0;
}

.month-chart h3 {
  margin: 0 0 12px;
  font-size: 14px;
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
  transition: height 0.3s ease;
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
  gap: 16px;
  margin-top: 12px;
}

.month-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
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
  padding: 1px 6px;
  border-radius: 10px;
  font-weight: 600;
}

.day-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-left: 8px;
}

.day-label {
  font-size: 13px;
  color: var(--tg-hint-color);
  margin: 0;
  text-transform: capitalize;
}

.event-card {
  background: var(--tg-secondary-bg-color);
  border-radius: 8px;
  padding: 10px 12px;
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
}

.event-text {
  flex: 1;
  word-break: break-word;
  font-size: 14px;
}

.importance {
  color: #ffc107;
  font-size: 12px;
  white-space: nowrap;
}
</style>
