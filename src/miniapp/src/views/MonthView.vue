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
import StarPicker from '../components/StarPicker.vue'

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const generating = ref(false)

const monthOffset = ref(0)

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

const importanceCounts = computed(() => {
  const counts = [0, 0, 0, 0, 0]
  for (const evt of events.value) {
    if (evt.importance >= 1 && evt.importance <= 5) {
      counts[evt.importance - 1] = (counts[evt.importance - 1] ?? 0) + 1
    }
  }
  return counts
})

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

async function fetchData() {
  loading.value = true
  error.value = null
  try {
    const { startStr, endStr } = monthRange.value
    const [eventsRes, summariesRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('monthly', { from: startStr, to: endStr, limit: 1 }),
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
    await runSummary('monthly', {
      period_start: monthRange.value.startStr,
      period_end: monthRange.value.endStr,
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
      <SummarySection
        title="Итог месяца"
        :status="summaryStatus"
        :event-count="summary?.content?.total_events"
        :generating="generating"
        @generate="handleGenerate"
      />

      <!-- Stats bar -->
      <div v-if="events.length" class="stats-bar">
        <span class="stats-bar__total">Всего: <strong>{{ events.length }}</strong></span>
        <div class="stats-bar__breakdown">
          <span
            v-for="(count, idx) in importanceCounts"
            :key="idx"
            v-show="count > 0"
            class="stats-bar__badge"
          >
            <StarPicker :model-value="idx + 1" readonly size="sm" />
            <span>{{ count }}</span>
          </span>
        </div>
      </div>

      <div v-if="groupedEvents.length" class="day-groups">
        <div v-for="group in groupedEvents" :key="group.date" class="day-group">
          <h4 class="day-label">{{ group.dateLabel }}</h4>
          <EventCard v-for="evt in group.events" :key="evt.id" :event="evt" />
        </div>
      </div>

      <EmptyState v-else message="Нет событий за этот месяц" icon="month" />
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
  text-transform: capitalize;
}

.stats-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background: var(--tg-secondary-bg-color);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
  border-radius: 12px;
  font-size: 13px;
  margin: 12px 0;
}

.stats-bar__breakdown {
  display: flex;
  gap: 10px;
}

.stats-bar__badge {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
}

.day-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
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
