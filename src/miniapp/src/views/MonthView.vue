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

// Month navigation offset
const monthOffset = ref(0)

const monthRange = computed(() => {
  const now = new Date()
  const year = now.getFullYear()
  const month = now.getMonth() + monthOffset.value

  const start = new Date(year, month, 1)
  const end = new Date(start.getFullYear(), start.getMonth() + 1, 0) // last day

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

// Count events by importance for chart-like display
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

function starsDisplay(n: number): string {
  return '★'.repeat(n) + '☆'.repeat(5 - n)
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

function prevMonth() {
  monthOffset.value--
}

function nextMonth() {
  monthOffset.value++
}

watch(monthOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="month-view">
    <div class="header">
      <h2>📆 Месяц</h2>
    </div>

    <!-- Month navigation -->
    <div class="month-nav">
      <button class="nav-btn" @click="prevMonth">◀</button>
      <span class="month-label">{{ monthLabel }}</span>
      <button class="nav-btn" @click="nextMonth" :disabled="monthOffset >= 0">▶</button>
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
        <h3>Итог месяца</h3>

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

      <!-- Stats bar -->
      <div v-if="events.length" class="stats-bar">
        <span>Всего событий: <strong>{{ events.length }}</strong></span>
        <span class="importance-stats">
          <span v-for="(count, idx) in importanceCounts" :key="idx" class="imp-badge" v-show="count > 0">
            {{ '★'.repeat(idx + 1) }} {{ count }}
          </span>
        </span>
      </div>

      <!-- Events grouped by day -->
      <div v-if="groupedEvents.length" class="day-groups">
        <div v-for="group in groupedEvents" :key="group.date" class="day-group">
          <h4 class="day-label">{{ group.dateLabel }}</h4>
          <div v-for="evt in group.events" :key="evt.id" class="event-card">
            <div class="event-text">{{ evt.text }}</div>
            <span class="importance">{{ starsDisplay(evt.importance) }}</span>
          </div>
        </div>
      </div>

      <div v-else class="empty">
        Нет событий за этот месяц
      </div>
    </template>
  </div>
</template>

<style scoped>
.month-view {
  max-width: 600px;
  margin: 0 auto;
}

.header h2 {
  margin: 0;
}

.month-nav {
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

.month-label {
  font-size: 15px;
  font-weight: 600;
  text-transform: capitalize;
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

.stats-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: var(--tg-secondary-bg-color);
  border-radius: 8px;
  font-size: 13px;
  margin-bottom: 12px;
}

.importance-stats {
  display: flex;
  gap: 8px;
}

.imp-badge {
  color: #ffc107;
  font-size: 11px;
}

.day-groups {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.day-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
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
