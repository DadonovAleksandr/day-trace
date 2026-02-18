<template>
  <div>
    <h1 class="page-title">Content</h1>

    <div class="tabs">
      <div class="tab" :class="{ active: activeTab === 'events' }" @click="activeTab = 'events'">Events</div>
      <div class="tab" :class="{ active: activeTab === 'summaries' }" @click="activeTab = 'summaries'">Summaries</div>
    </div>

    <!-- Events Tab -->
    <div v-if="activeTab === 'events'">
      <div class="filters">
        <input v-model="eventsFilters.user_id" placeholder="User ID" type="number" style="width: 100px" />
        <input v-model="eventsFilters.from" placeholder="From (YYYY-MM-DD)" type="date" />
        <input v-model="eventsFilters.to" placeholder="To (YYYY-MM-DD)" type="date" />
        <select v-model="eventsFilters.importance">
          <option value="">All importance</option>
          <option v-for="i in 5" :key="i" :value="i">{{ '★'.repeat(i) }}</option>
        </select>
        <button class="btn btn-primary btn-sm" @click="loadEvents">Search</button>
      </div>

      <p v-if="eventsLoading" class="loading">Loading events...</p>
      <p v-if="eventsError" class="error-text">{{ eventsError }}</p>

      <div v-if="!eventsLoading" class="card">
        <table class="data-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>User</th>
              <th>Date</th>
              <th>Importance</th>
              <th>Text</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="event in events" :key="event.id">
              <td style="font-family: monospace; font-size: 0.75rem">{{ event.id.substring(0, 8) }}…</td>
              <td>{{ event.user_id }}</td>
              <td>{{ event.local_date }}</td>
              <td>{{ '★'.repeat(event.importance) }}</td>
              <td style="max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ event.text }}</td>
              <td>{{ formatDate(event.created_at) }}</td>
            </tr>
            <tr v-if="events.length === 0">
              <td colspan="6" style="text-align: center; color: var(--text-secondary)">No events found</td>
            </tr>
          </tbody>
        </table>

        <div class="pagination">
          <button class="btn btn-sm" :disabled="eventsOffset === 0" @click="eventsOffset = Math.max(0, eventsOffset - eventsLimit); loadEvents()">← Prev</button>
          <span>{{ eventsOffset + 1 }}–{{ Math.min(eventsOffset + eventsLimit, eventsTotal) }} of {{ eventsTotal }}</span>
          <button class="btn btn-sm" :disabled="eventsOffset + eventsLimit >= eventsTotal" @click="eventsOffset += eventsLimit; loadEvents()">Next →</button>
        </div>
      </div>
    </div>

    <!-- Summaries Tab -->
    <div v-if="activeTab === 'summaries'">
      <div class="filters">
        <input v-model="summariesFilters.user_id" placeholder="User ID" type="number" style="width: 100px" />
        <select v-model="summariesFilters.period_type">
          <option value="">All types</option>
          <option value="weekly">Weekly</option>
          <option value="monthly">Monthly</option>
          <option value="yearly">Yearly</option>
        </select>
        <select v-model="summariesFilters.status">
          <option value="">All statuses</option>
          <option value="generated">Generated</option>
          <option value="generating">Generating</option>
          <option value="failed">Failed</option>
        </select>
        <input v-model="summariesFilters.from" type="date" placeholder="From" />
        <input v-model="summariesFilters.to" type="date" placeholder="To" />
        <button class="btn btn-primary btn-sm" @click="loadSummaries">Search</button>
      </div>

      <p v-if="summariesLoading" class="loading">Loading summaries...</p>
      <p v-if="summariesError" class="error-text">{{ summariesError }}</p>

      <div v-if="!summariesLoading" class="card">
        <table class="data-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>User</th>
              <th>Type</th>
              <th>Period</th>
              <th>Status</th>
              <th>Version</th>
              <th>Events</th>
              <th>Generated</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="summary in summaries" :key="summary.id">
              <td>{{ summary.id }}</td>
              <td>{{ summary.user_id }}</td>
              <td><span class="badge badge-info">{{ summary.period_type }}</span></td>
              <td>{{ summary.period_start }} → {{ summary.period_end }}</td>
              <td>
                <span :class="['badge', statusBadge(summary.status)]">{{ summary.status }}</span>
              </td>
              <td>v{{ summary.version }}</td>
              <td>{{ summary.content?.total_events ?? '—' }}</td>
              <td>{{ summary.last_generated_at ? formatDate(summary.last_generated_at) : '—' }}</td>
            </tr>
            <tr v-if="summaries.length === 0">
              <td colspan="8" style="text-align: center; color: var(--text-secondary)">No summaries found</td>
            </tr>
          </tbody>
        </table>

        <div class="pagination">
          <button class="btn btn-sm" :disabled="summariesOffset === 0" @click="summariesOffset = Math.max(0, summariesOffset - summariesLimit); loadSummaries()">← Prev</button>
          <span>{{ summariesOffset + 1 }}–{{ Math.min(summariesOffset + summariesLimit, summariesTotal) }} of {{ summariesTotal }}</span>
          <button class="btn btn-sm" :disabled="summariesOffset + summariesLimit >= summariesTotal" @click="summariesOffset += summariesLimit; loadSummaries()">Next →</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { getEvents, getSummaries } from '../api/admin'
import type { EventItem, SummaryItem } from '../types'

const activeTab = ref<'events' | 'summaries'>('events')

// Events
const events = ref<EventItem[]>([])
const eventsTotal = ref(0)
const eventsLoading = ref(false)
const eventsError = ref('')
const eventsLimit = 20
const eventsOffset = ref(0)
const eventsFilters = ref<{ user_id?: string; from?: string; to?: string; importance?: string }>({})

async function loadEvents() {
  eventsLoading.value = true
  eventsError.value = ''
  try {
    const params: any = { limit: eventsLimit, offset: eventsOffset.value }
    if (eventsFilters.value.user_id) params.user_id = Number(eventsFilters.value.user_id)
    if (eventsFilters.value.from) params.from = eventsFilters.value.from
    if (eventsFilters.value.to) params.to = eventsFilters.value.to
    if (eventsFilters.value.importance) params.importance = Number(eventsFilters.value.importance)
    const res = await getEvents(params)
    events.value = res.items
    eventsTotal.value = res.total
  } catch (e: any) {
    eventsError.value = e.response?.data?.message || 'Failed to load events'
  } finally {
    eventsLoading.value = false
  }
}

// Summaries
const summaries = ref<SummaryItem[]>([])
const summariesTotal = ref(0)
const summariesLoading = ref(false)
const summariesError = ref('')
const summariesLimit = 20
const summariesOffset = ref(0)
const summariesFilters = ref<{ user_id?: string; period_type?: string; status?: string; from?: string; to?: string }>({})

async function loadSummaries() {
  summariesLoading.value = true
  summariesError.value = ''
  try {
    const params: any = { limit: summariesLimit, offset: summariesOffset.value }
    if (summariesFilters.value.user_id) params.user_id = Number(summariesFilters.value.user_id)
    if (summariesFilters.value.period_type) params.period_type = summariesFilters.value.period_type
    if (summariesFilters.value.status) params.status = summariesFilters.value.status
    if (summariesFilters.value.from) params.from = summariesFilters.value.from
    if (summariesFilters.value.to) params.to = summariesFilters.value.to
    const res = await getSummaries(params)
    summaries.value = res.items
    summariesTotal.value = res.total
  } catch (e: any) {
    summariesError.value = e.response?.data?.message || 'Failed to load summaries'
  } finally {
    summariesLoading.value = false
  }
}

function statusBadge(status: string): string {
  switch (status) {
    case 'generated': return 'badge-success'
    case 'generating': return 'badge-warning'
    case 'failed': return 'badge-danger'
    default: return 'badge-gray'
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

watch(activeTab, (tab) => {
  if (tab === 'events' && events.value.length === 0) loadEvents()
  if (tab === 'summaries' && summaries.value.length === 0) loadSummaries()
})

onMounted(loadEvents)
</script>
