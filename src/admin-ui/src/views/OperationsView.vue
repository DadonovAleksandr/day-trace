<template>
  <div>
    <h1 class="page-title">Operations</h1>

    <div class="tabs">
      <div class="tab" :class="{ active: activeTab === 'jobs' }" @click="activeTab = 'jobs'">Period Jobs</div>
      <div class="tab" :class="{ active: activeTab === 'deliveries' }" @click="activeTab = 'deliveries'">Delivery Attempts</div>
    </div>

    <!-- Period Jobs Tab -->
    <div v-if="activeTab === 'jobs'">
      <div class="filters">
        <input v-model="jobsFilters.user_id" placeholder="User ID" type="number" style="width: 100px" />
        <select v-model="jobsFilters.status">
          <option value="">All statuses</option>
          <option value="pending">Pending</option>
          <option value="running">Running</option>
          <option value="failed">Failed</option>
          <option value="retried">Retried</option>
          <option value="succeeded">Succeeded</option>
          <option value="superseded">Superseded</option>
        </select>
        <button class="btn btn-primary btn-sm" @click="loadJobs">Search</button>
      </div>

      <p v-if="jobsLoading" class="loading">Loading jobs...</p>
      <p v-if="jobsError" class="error-text">{{ jobsError }}</p>

      <div v-if="!jobsLoading" class="card">
        <table class="data-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>User</th>
              <th>Type</th>
              <th>Period</th>
              <th>Run #</th>
              <th>Status</th>
              <th>Attempts</th>
              <th>Started</th>
              <th>Finished</th>
              <th>Error</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="job in jobs" :key="job.id" :class="{ 'stuck-row': job.is_stuck }">
              <td>{{ job.id }}</td>
              <td>{{ job.user_id }}</td>
              <td><span class="badge badge-info">{{ job.period_type }}</span></td>
              <td>{{ job.period_start }} → {{ job.period_end }}</td>
              <td>{{ job.run_number }}</td>
              <td>
                <span :class="['badge', jobStatusBadge(job.status)]">
                  {{ job.status }}
                  <template v-if="job.is_stuck"> ⚠️</template>
                </span>
              </td>
              <td>{{ job.attempt_count }}</td>
              <td>{{ job.started_at ? formatDate(job.started_at) : '—' }}</td>
              <td>{{ job.finished_at ? formatDate(job.finished_at) : '—' }}</td>
              <td style="max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap" :title="job.error || ''">
                {{ job.error || '—' }}
              </td>
            </tr>
            <tr v-if="jobs.length === 0">
              <td colspan="10" style="text-align: center; color: var(--text-secondary)">No jobs found</td>
            </tr>
          </tbody>
        </table>

        <div class="pagination">
          <button class="btn btn-sm" :disabled="jobsOffset === 0" @click="jobsOffset = Math.max(0, jobsOffset - jobsLimit); loadJobs()">← Prev</button>
          <span>{{ jobsOffset + 1 }}–{{ Math.min(jobsOffset + jobsLimit, jobsTotal) }} of {{ jobsTotal }}</span>
          <button class="btn btn-sm" :disabled="jobsOffset + jobsLimit >= jobsTotal" @click="jobsOffset += jobsLimit; loadJobs()">Next →</button>
        </div>
      </div>
    </div>

    <!-- Delivery Attempts Tab -->
    <div v-if="activeTab === 'deliveries'">
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
          <span>{{ deliveriesOffset + 1 }}–{{ Math.min(deliveriesOffset + deliveriesLimit, deliveriesTotal) }} of {{ deliveriesTotal }}</span>
          <button class="btn btn-sm" :disabled="deliveriesOffset + deliveriesLimit >= deliveriesTotal" @click="deliveriesOffset += deliveriesLimit; loadDeliveries()">Next →</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { getPeriodJobs, getDeliveryAttempts } from '../api/admin'
import type { PeriodJobItem, DeliveryAttemptItem } from '../types'

const activeTab = ref<'jobs' | 'deliveries'>('jobs')

// Period Jobs
const jobs = ref<PeriodJobItem[]>([])
const jobsTotal = ref(0)
const jobsLoading = ref(false)
const jobsError = ref('')
const jobsLimit = 20
const jobsOffset = ref(0)
const jobsFilters = ref<{ user_id?: string; status?: string }>({})

async function loadJobs() {
  jobsLoading.value = true
  jobsError.value = ''
  try {
    const params: any = { limit: jobsLimit, offset: jobsOffset.value }
    if (jobsFilters.value.user_id) params.user_id = Number(jobsFilters.value.user_id)
    if (jobsFilters.value.status) params.status = jobsFilters.value.status
    const res = await getPeriodJobs(params)
    jobs.value = res.items
    jobsTotal.value = res.total
  } catch (e: any) {
    jobsError.value = e.response?.data?.message || 'Failed to load jobs'
  } finally {
    jobsLoading.value = false
  }
}

// Delivery Attempts
const deliveries = ref<DeliveryAttemptItem[]>([])
const deliveriesTotal = ref(0)
const deliveriesLoading = ref(false)
const deliveriesError = ref('')
const deliveriesLimit = 20
const deliveriesOffset = ref(0)
const deliveriesFilters = ref<{ user_id?: string; status?: string; delivery_type?: string }>({})

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

function jobStatusBadge(status: string): string {
  switch (status) {
    case 'succeeded': return 'badge-success'
    case 'running': return 'badge-warning'
    case 'pending': case 'retried': return 'badge-info'
    case 'failed': return 'badge-danger'
    case 'superseded': return 'badge-gray'
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

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

watch(activeTab, (tab) => {
  if (tab === 'jobs' && jobs.value.length === 0) loadJobs()
  if (tab === 'deliveries' && deliveries.value.length === 0) loadDeliveries()
})

onMounted(loadJobs)
</script>

<style scoped>
.stuck-row td {
  background: #fef3c7 !important;
}
</style>
