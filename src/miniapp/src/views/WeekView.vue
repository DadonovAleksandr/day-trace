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

// Week navigation offset (0 = current, -1 = previous, etc.)
const weekOffset = ref(0)

const weekRange = computed(() => {
  const now = new Date()
  const day = now.getDay()
  // Calculate start of current week (Monday)
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

// Group events by local_date
const groupedEvents = computed(() => {
  const groups: Record<string, EventItem[]> = {}
  for (const evt of events.value) {
    if (!groups[evt.local_date]) groups[evt.local_date] = []
    groups[evt.local_date].push(evt)
  }
  // Sort dates ascending, events by importance desc within each date
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

function starsDisplay(n: number): string {
  return '★'.repeat(n) + '☆'.repeat(5 - n)
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
    summary.value = summariesRes.items.length > 0 ? summariesRes.items[0] : null
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

function prevWeek() {
  weekOffset.value--
}

function nextWeek() {
  weekOffset.value++
}

watch(weekOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="week-view">
    <div class="header">
      <h2>📅 Неделя</h2>
    </div>

    <!-- Week navigation -->
    <div class="week-nav">
      <button class="nav-btn" @click="prevWeek">◀</button>
      <span class="week-label">{{ weekLabel }}</span>
      <button class="nav-btn" @click="nextWeek" :disabled="weekOffset >= 0">▶</button>
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
        <h3>Итог недели</h3>
        <div v-if="summaryStatus === 'generated'" class="summary-content">
          <p class="summary-meta">
            ✅ {{ summary!.content?.total_events || 0 }} событий
          </p>
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

      <!-- Events by day -->
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
        Нет событий за эту неделю
      </div>
    </template>
  </div>
</template>

<style scoped>
.week-view {
  max-width: 600px;
  margin: 0 auto;
}

.header h2 {
  margin: 0;
}

.week-nav {
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

.week-label {
  font-size: 15px;
  font-weight: 600;
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
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.generate-btn:disabled {
  opacity: 0.5;
}

.btn-primary {
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.day-groups {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 12px;
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
