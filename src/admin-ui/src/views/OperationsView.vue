<template>
  <div>
    <h1 class="page-title">Operations</h1>

    <div class="card" style="margin-bottom: 1rem">
      <div style="display: grid; gap: 0.75rem">
        <div style="display: flex; justify-content: space-between; gap: 0.75rem; align-items: center; flex-wrap: wrap">
          <div>
            <h2 style="margin: 0; font-size: 1rem">Mass Broadcast</h2>
            <p style="margin: 0.2rem 0 0; color: var(--text-secondary); font-size: 0.85rem">
              Queue a one-off message to selected audience.
            </p>
          </div>
          <div style="display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap">
            <select v-model="broadcastForm.audience">
              <option value="active">Active users</option>
              <option value="reminders">Reminder-enabled users</option>
            </select>
            <label style="display: flex; align-items: center; gap: 0.4rem; font-size: 0.85rem; color: var(--text-secondary)">
              <input v-model="broadcastForm.confirmed" type="checkbox" />
              Confirm send
            </label>
          </div>
        </div>

        <textarea
          v-model="broadcastForm.text"
          rows="4"
          placeholder="Broadcast message text..."
          style="width: 100%; resize: vertical; min-height: 96px"
        />

        <div style="display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap">
          <button
            class="btn btn-primary btn-sm"
            :disabled="broadcastSending"
            @click="handleSendBroadcast"
          >
            {{ broadcastSending ? 'Queueing...' : 'Queue Broadcast' }}
          </button>
          <span style="font-size: 0.85rem; color: var(--text-secondary)">
            {{ broadcastForm.text.trim().length }} chars
          </span>
        </div>

        <p v-if="broadcastError" class="error-text" style="margin: 0">{{ broadcastError }}</p>

        <div
          v-if="broadcastResult"
          style="padding: 0.75rem; border-radius: 8px; background: rgba(59, 130, 246, 0.08); color: var(--text-primary)"
        >
          Broadcast queued: campaign #{{ broadcastResult.campaign_id }}, status
          <strong>{{ broadcastResult.status }}</strong>, queued {{ broadcastResult.queued_count }}
          (audience: {{ broadcastResult.audience }})
        </div>
      </div>
    </div>

    <div class="card" style="margin-bottom: 1rem">
      <div style="display: grid; gap: 0.75rem">
        <div style="display: flex; justify-content: space-between; gap: 0.75rem; align-items: center; flex-wrap: wrap">
          <div>
            <h2 style="margin: 0; font-size: 1rem">Broadcast Campaigns</h2>
            <p style="margin: 0.2rem 0 0; color: var(--text-secondary); font-size: 0.85rem">
              Latest queued campaigns and delivery progress.
            </p>
          </div>
          <button class="btn btn-sm" :disabled="campaignsLoading" @click="loadBroadcastCampaigns">
            {{ campaignsLoading ? 'Refreshing...' : 'Refresh' }}
          </button>
        </div>

        <p v-if="campaignsError" class="error-text" style="margin: 0">{{ campaignsError }}</p>
        <p v-else-if="campaignsLoading && campaigns.length === 0" class="loading" style="margin: 0">Loading campaigns...</p>

        <table v-if="!campaignsLoading || campaigns.length > 0" class="data-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>Audience</th>
              <th>Status</th>
              <th>Queued</th>
              <th>Pending</th>
              <th>Sent</th>
              <th>Failed</th>
              <th>Terminal Failed</th>
              <th>Created</th>
              <th>Completed</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="campaign in campaigns" :key="campaign.id">
              <td>{{ campaign.id }}</td>
              <td><span class="badge badge-gray">{{ campaign.audience }}</span></td>
              <td><span :class="['badge', campaignStatusBadge(campaign.status)]">{{ campaign.status }}</span></td>
              <td>{{ formatCount(campaign.queued_count) }}</td>
              <td>{{ formatCount(campaign.pending_count) }}</td>
              <td>{{ formatCount(campaign.sent_count) }}</td>
              <td>{{ formatCount(campaign.failed_count) }}</td>
              <td>{{ formatCount(campaign.terminal_failed_count) }}</td>
              <td>{{ campaign.created_at ? formatDate(campaign.created_at) : '—' }}</td>
              <td>{{ campaign.completed_at ? formatDate(campaign.completed_at) : '—' }}</td>
            </tr>
            <tr v-if="campaigns.length === 0">
              <td colspan="10" style="text-align: center; color: var(--text-secondary)">No broadcast campaigns found</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div class="filters">
      <input v-model="deliveriesFilters.user_id" placeholder="User ID" type="number" style="width: 100px" />
      <select v-model="deliveriesFilters.status">
        <option value="">All statuses</option>
        <option value="pending">Pending</option>
        <option value="sent">Sent</option>
        <option value="failed">Failed</option>
        <option value="terminal_failed">Terminal Failed</option>
      </select>
      <select v-model="deliveriesFilters.delivery_type">
        <option value="">All types</option>
        <option value="reminder">Reminder</option>
        <option value="summary_notification">Summary Notification</option>
        <option value="soft_reminder">Soft Reminder</option>
        <option value="admin_reply">Admin Reply</option>
        <option value="admin_broadcast">Admin Broadcast</option>
      </select>
      <button class="btn btn-primary btn-sm" @click="loadDeliveries">Search</button>
    </div>

    <p v-if="deliveriesLoading" class="loading">Loading delivery attempts...</p>
    <p v-if="deliveriesError" class="error-text">{{ deliveriesError }}</p>

    <div v-if="!deliveriesLoading" class="card">
      <table class="data-table">
        <thead>
          <tr>
            <th>ID</th>
            <th>User</th>
            <th>Type</th>
            <th>Attempt #</th>
            <th>Status</th>
            <th>TG Msg ID</th>
            <th>Scheduled</th>
            <th>Sent</th>
            <th>Error</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="attempt in deliveries" :key="attempt.id">
            <td>{{ attempt.id }}</td>
            <td>{{ attempt.user_id }}</td>
            <td><span class="badge badge-gray">{{ attempt.delivery_type }}</span></td>
            <td>{{ attempt.attempt_number }}</td>
            <td>
              <span :class="['badge', deliveryStatusBadge(attempt.status)]">{{ attempt.status }}</span>
            </td>
            <td>{{ attempt.telegram_message_id || '—' }}</td>
            <td>{{ attempt.scheduled_at ? formatDate(attempt.scheduled_at) : '—' }}</td>
            <td>{{ attempt.sent_at ? formatDate(attempt.sent_at) : '—' }}</td>
            <td style="max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap" :title="attempt.error_message || ''">
              {{ attempt.error_message || '—' }}
            </td>
          </tr>
          <tr v-if="deliveries.length === 0">
            <td colspan="9" style="text-align: center; color: var(--text-secondary)">No delivery attempts found</td>
          </tr>
        </tbody>
      </table>

      <div class="pagination">
        <button class="btn btn-sm" :disabled="deliveriesOffset === 0" @click="deliveriesOffset = Math.max(0, deliveriesOffset - deliveriesLimit); loadDeliveries()">← Prev</button>
        <span>{{ deliveriesTotal === 0 ? 0 : deliveriesOffset + 1 }}–{{ Math.min(deliveriesOffset + deliveriesLimit, deliveriesTotal) }} of {{ deliveriesTotal }}</span>
        <button class="btn btn-sm" :disabled="deliveriesOffset + deliveriesLimit >= deliveriesTotal" @click="deliveriesOffset += deliveriesLimit; loadDeliveries()">Next →</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getAdminBroadcastCampaigns, getDeliveryAttempts, sendAdminBroadcast } from '../api/admin'
import type { AdminBroadcastAudience, AdminBroadcastCampaignItem, AdminBroadcastResponse, DeliveryAttemptItem } from '../types'

const broadcastForm = ref<{ audience: AdminBroadcastAudience; text: string; confirmed: boolean }>({
  audience: 'active',
  text: '',
  confirmed: false,
})
const broadcastSending = ref(false)
const broadcastError = ref('')
const broadcastResult = ref<AdminBroadcastResponse | null>(null)

const campaigns = ref<AdminBroadcastCampaignItem[]>([])
const campaignsLoading = ref(false)
const campaignsError = ref('')
const campaignsLimit = 10

const deliveries = ref<DeliveryAttemptItem[]>([])
const deliveriesTotal = ref(0)
const deliveriesLoading = ref(false)
const deliveriesError = ref('')
const deliveriesLimit = 20
const deliveriesOffset = ref(0)
const deliveriesFilters = ref<{ user_id?: string; status?: string; delivery_type?: string }>({})

async function handleSendBroadcast() {
  const text = broadcastForm.value.text.trim()
  if (!text) {
    broadcastError.value = 'Broadcast text is required'
    return
  }

  if (!broadcastForm.value.confirmed) {
    broadcastError.value = 'Confirm the broadcast before sending'
    return
  }

  broadcastSending.value = true
  broadcastError.value = ''

  try {
    const result = await sendAdminBroadcast({
      audience: broadcastForm.value.audience,
      text,
    })
    broadcastResult.value = result
    broadcastForm.value.text = ''
    broadcastForm.value.confirmed = false
    await Promise.all([loadBroadcastCampaigns(), loadDeliveries()])
  } catch (e: any) {
    broadcastError.value = e.response?.data?.message || 'Failed to queue broadcast'
  } finally {
    broadcastSending.value = false
  }
}

async function loadBroadcastCampaigns() {
  campaignsLoading.value = true
  campaignsError.value = ''
  try {
    const res = await getAdminBroadcastCampaigns({ limit: campaignsLimit, offset: 0 })
    campaigns.value = res.items
  } catch (e: any) {
    campaignsError.value = e.response?.data?.message || 'Failed to load broadcast campaigns'
  } finally {
    campaignsLoading.value = false
  }
}

async function loadDeliveries() {
  deliveriesLoading.value = true
  deliveriesError.value = ''
  try {
    const params: any = { limit: deliveriesLimit, offset: deliveriesOffset.value }
    if (deliveriesFilters.value.user_id) params.user_id = Number(deliveriesFilters.value.user_id)
    if (deliveriesFilters.value.status) params.status = deliveriesFilters.value.status
    if (deliveriesFilters.value.delivery_type) params.delivery_type = deliveriesFilters.value.delivery_type
    const res = await getDeliveryAttempts(params)
    deliveries.value = res.items
    deliveriesTotal.value = res.total
  } catch (e: any) {
    deliveriesError.value = e.response?.data?.message || 'Failed to load delivery attempts'
  } finally {
    deliveriesLoading.value = false
  }
}

function campaignStatusBadge(status: string): string {
  switch (status) {
    case 'completed': return 'badge-success'
    case 'queued': return 'badge-warning'
    case 'processing': return 'badge-warning'
    case 'partial_failed': return 'badge-danger'
    case 'failed': return 'badge-danger'
    default: return 'badge-gray'
  }
}

function deliveryStatusBadge(status: string): string {
  switch (status) {
    case 'sent': return 'badge-success'
    case 'pending': return 'badge-warning'
    case 'failed': return 'badge-danger'
    case 'terminal_failed': return 'badge-danger'
    default: return 'badge-gray'
  }
}

function formatCount(value?: number | null): string {
  return typeof value === 'number' ? String(value) : '—'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

onMounted(async () => {
  await Promise.all([loadBroadcastCampaigns(), loadDeliveries()])
})
</script>
