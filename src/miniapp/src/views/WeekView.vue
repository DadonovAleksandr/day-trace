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

const weekOffset = ref(0)

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

const groupedEvents = computed(() => {
  const groups: Record<string, EventItem[]> = {}
  for (const evt of events.value) {
    if (!groups[evt.local_date]) groups[evt.local_date] = []
    groups[evt.local_date]!.push(evt)
  }
  const sorted = Object.entries(groups).sort(([a], [b]) => a.localeCompare(b))
  return sorted.map(([date, items]) => ({
    date,
    dateLabel: new Date(date + 'T00:00:00').toLocaleDateString('ru-RU', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
    }),
    events: items.sort((a, b) => b.importance - a.importance),
  }))
})

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
    const { startStr, endStr } = weekRange.value
    const [eventsRes, summariesRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('weekly', { from: startStr, to: endStr, limit: 1 }),
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
    await runSummary('weekly', {
      period_start: weekRange.value.startStr,
      period_end: weekRange.value.endStr,
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
      <SummarySection
        title="Итог недели"
        :status="summaryStatus"
        :event-count="summary?.content?.total_events"
        :generating="generating"
        @generate="handleGenerate"
      />

      <div v-if="groupedEvents.length" class="day-groups">
        <div v-for="group in groupedEvents" :key="group.date" class="day-group">
          <h4 class="day-label">{{ group.dateLabel }}</h4>
          <EventCard v-for="evt in group.events" :key="evt.id" :event="evt" />
        </div>
      </div>

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
}

.day-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
  margin-top: 14px;
}

.day-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
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
