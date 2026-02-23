<template>
  <div>
    <h1 class="page-title">Content</h1>

    <div class="tabs">
      <div class="tab" :class="{ active: activeTab === 'events' }" @click="activeTab = 'events'">Events</div>
      <div class="tab" :class="{ active: activeTab === 'summaries' }" @click="activeTab = 'summaries'">Summaries</div>
      <div class="tab" :class="{ active: activeTab === 'feedback' }" @click="activeTab = 'feedback'">Feedback</div>
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

    <!-- Feedback Tab -->
    <div v-if="activeTab === 'feedback'">
      <div class="filters">
        <input v-model="feedbackFilters.user_id" placeholder="User ID" type="number" style="width: 100px" />
        <select v-model="feedbackFilters.status">
          <option value="">All statuses</option>
          <option value="new">New</option>
          <option value="read">Read</option>
          <option value="responded">Responded</option>
        </select>
        <input v-model="feedbackFilters.from" type="date" placeholder="From" />
        <input v-model="feedbackFilters.to" type="date" placeholder="To" />
        <button class="btn btn-primary btn-sm" @click="loadFeedback">Search</button>
      </div>

      <p v-if="feedbackLoading" class="loading">Loading feedback...</p>
      <p v-if="feedbackError" class="error-text">{{ feedbackError }}</p>

      <div v-if="!feedbackLoading" class="card">
        <table class="data-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>User</th>
              <th>Telegram</th>
              <th>Text</th>
              <th>Status</th>
              <th>Created</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="fb in feedbackItems" :key="fb.id">
              <tr>
                <td>{{ fb.id }}</td>
                <td>{{ fb.user_id }}</td>
                <td>{{ fb.telegram_user_id ?? '—' }}</td>
                <td style="max-width: 400px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">{{ fb.text }}</td>
                <td>
                  <span :class="['badge', feedbackStatusBadge(fb.status)]">{{ fb.status }}</span>
                </td>
                <td>{{ formatDate(fb.created_at) }}</td>
                <td>
                  <div style="display: flex; flex-wrap: wrap; gap: 0.4rem; align-items: center">
                    <button
                      v-if="fb.status === 'new'"
                      class="btn btn-sm btn-primary"
                      :disabled="markingReadId === fb.id"
                      @click="handleMarkRead(fb.id)"
                    >
                      {{ markingReadId === fb.id ? '...' : 'Mark Read' }}
                    </button>
                    <button
                      class="btn btn-sm"
                      :disabled="replySendingId === fb.id"
                      @click="toggleReplyForm(fb.id)"
                    >
                      {{ replyingFeedbackId === fb.id ? 'Cancel' : 'Reply' }}
                    </button>
                    <span style="color: var(--text-secondary); font-size: 0.85rem">
                      {{ fb.read_at ? `Read: ${formatDate(fb.read_at)}` : 'Unread' }}
                    </span>
                  </div>
                </td>
              </tr>
              <tr v-if="replyingFeedbackId === fb.id">
                <td colspan="7" style="background: rgba(148, 163, 184, 0.06)">
                  <div style="display: grid; gap: 0.5rem; padding: 0.5rem 0">
                    <textarea
                      v-model="replyDrafts[fb.id]"
                      rows="3"
                      placeholder="Reply message..."
                      style="width: 100%; resize: vertical; min-height: 72px"
                    />
                    <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap">
                      <button
                        class="btn btn-primary btn-sm"
                        :disabled="replySendingId === fb.id"
                        @click="handleSendReply(fb)"
                      >
                        {{ replySendingId === fb.id ? 'Sending...' : 'Send Reply' }}
                      </button>
                      <button class="btn btn-sm" :disabled="replySendingId === fb.id" @click="toggleReplyForm(fb.id)">
                        Close
                      </button>
                      <span v-if="replyErrors[fb.id]" class="error-text" style="margin: 0">{{ replyErrors[fb.id] }}</span>
                    </div>
                  </div>
                </td>
              </tr>
            </template>
            <tr v-if="feedbackItems.length === 0">
              <td colspan="7" style="text-align: center; color: var(--text-secondary)">No feedback found</td>
            </tr>
          </tbody>
        </table>

        <div class="pagination">
          <button class="btn btn-sm" :disabled="feedbackOffset === 0" @click="feedbackOffset = Math.max(0, feedbackOffset - feedbackLimit); loadFeedback()">← Prev</button>
          <span>{{ feedbackOffset + 1 }}–{{ Math.min(feedbackOffset + feedbackLimit, feedbackTotal) }} of {{ feedbackTotal }}</span>
          <button class="btn btn-sm" :disabled="feedbackOffset + feedbackLimit >= feedbackTotal" @click="feedbackOffset += feedbackLimit; loadFeedback()">Next →</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { getEvents, getSummaries, getFeedback, markFeedbackRead, replyToFeedback } from '../api/admin'
import type { EventItem, SummaryItem, FeedbackItem } from '../types'

const activeTab = ref<'events' | 'summaries' | 'feedback'>('events')

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

// Feedback
const feedbackItems = ref<FeedbackItem[]>([])
const feedbackTotal = ref(0)
const feedbackLoading = ref(false)
const feedbackError = ref('')
const feedbackLimit = 20
const feedbackOffset = ref(0)
const feedbackFilters = ref<{ user_id?: string; status?: string; from?: string; to?: string }>({})
const markingReadId = ref<number | null>(null)
const replyingFeedbackId = ref<number | null>(null)
const replySendingId = ref<number | null>(null)
const replyDrafts = ref<Record<number, string>>({})
const replyErrors = ref<Record<number, string>>({})

async function loadFeedback() {
  feedbackLoading.value = true
  feedbackError.value = ''
  try {
    const params: any = { limit: feedbackLimit, offset: feedbackOffset.value }
    if (feedbackFilters.value.user_id) params.user_id = Number(feedbackFilters.value.user_id)
    if (feedbackFilters.value.status) params.status = feedbackFilters.value.status
    if (feedbackFilters.value.from) params.from = feedbackFilters.value.from
    if (feedbackFilters.value.to) params.to = feedbackFilters.value.to
    const res = await getFeedback(params)
    feedbackItems.value = res.items
    feedbackTotal.value = res.total
    feedbackError.value = ''
  } catch (e: any) {
    feedbackError.value = e.response?.data?.message || 'Failed to load feedback'
  } finally {
    feedbackLoading.value = false
  }
}

async function handleMarkRead(id: number) {
  markingReadId.value = id
  try {
    const result = await markFeedbackRead(id)
    const item = feedbackItems.value.find(f => f.id === id)
    if (item) {
      item.status = result.status || 'read'
      item.read_at = result.read_at || item.read_at || new Date().toISOString()
    }
  } catch (e: any) {
    feedbackError.value = e.response?.data?.message || 'Failed to mark as read'
  } finally {
    markingReadId.value = null
  }
}

function toggleReplyForm(id: number) {
  if (replyingFeedbackId.value === id) {
    replyingFeedbackId.value = null
    return
  }

  replyingFeedbackId.value = id
  replyErrors.value[id] = ''
  if (typeof replyDrafts.value[id] !== 'string') {
    replyDrafts.value[id] = ''
  }
}

async function handleSendReply(item: FeedbackItem) {
  const text = (replyDrafts.value[item.id] || '').trim()
  if (!text) {
    replyErrors.value[item.id] = 'Reply text is required'
    return
  }

  replySendingId.value = item.id
  replyErrors.value[item.id] = ''
  feedbackError.value = ''

  try {
    const result = await replyToFeedback(item.id, { text })
    item.status = 'responded'
    item.read_at = result.read_at || item.read_at || new Date().toISOString()
    replyDrafts.value[item.id] = ''
    replyingFeedbackId.value = null
  } catch (e: any) {
    replyErrors.value[item.id] = e.response?.data?.message || 'Failed to send reply'
  } finally {
    replySendingId.value = null
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

function feedbackStatusBadge(status: string): string {
  switch (status) {
    case 'new': return 'badge-warning'
    case 'read': return 'badge-success'
    case 'responded': return 'badge-info'
    default: return 'badge-gray'
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

watch(activeTab, (tab) => {
  if (tab === 'events' && events.value.length === 0) loadEvents()
  if (tab === 'summaries' && summaries.value.length === 0) loadSummaries()
  if (tab === 'feedback' && feedbackItems.value.length === 0) loadFeedback()
})

onMounted(loadEvents)
</script>
