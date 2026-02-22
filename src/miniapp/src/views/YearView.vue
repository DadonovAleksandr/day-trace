<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, runSummary } from '../api/summaries'
import EventCard from '../components/EventCard.vue'
import PeriodNav from '../components/PeriodNav.vue'
import SummarySection from '../components/SummarySection.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const generating = ref(false)

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

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
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
            <EventCard v-for="evt in dayGroup.events" :key="evt.id" :event="evt" />
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
</style>
