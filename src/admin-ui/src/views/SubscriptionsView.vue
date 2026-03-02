<template>
  <div>
    <h1 class="page-title">Subscriptions</h1>

    <div class="filters">
      <select v-model="statusFilter" @change="offset = 0; load()">
        <option value="">All statuses</option>
        <option value="not_started">Not started</option>
        <option value="trial">Trial</option>
        <option value="active">Active</option>
        <option value="grace_period">Grace period</option>
        <option value="expired">Expired</option>
        <option value="exempt">Exempt</option>
      </select>
    </div>

    <p v-if="loading" class="loading">Loading subscriptions...</p>
    <p v-if="error" class="error-text">{{ error }}</p>

    <div class="subscriptions-layout">
      <div class="subscriptions-table-wrapper">
        <div v-if="!loading" class="card">
          <table class="data-table">
            <thead>
              <tr>
                <th>User ID</th>
                <th>Telegram ID</th>
                <th>Status</th>
                <th>Trial until</th>
                <th>Subscription until</th>
                <th>Exempt</th>
                <th>Days left</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="item in items"
                :key="item.user_id"
                :class="{ 'row-selected': selectedUserId === item.user_id }"
                style="cursor: pointer"
                @click="selectUser(item.user_id)"
              >
                <td>{{ item.user_id }}</td>
                <td>{{ item.telegram_id }}</td>
                <td><span :class="['badge', statusBadgeClass(item.status)]">{{ statusLabel(item.status) }}</span></td>
                <td>{{ item.trial_expires_at ? formatDate(item.trial_expires_at) : '—' }}</td>
                <td>{{ item.subscription_expires_at ? formatDate(item.subscription_expires_at) : '—' }}</td>
                <td>{{ item.is_exempt ? 'Yes' : 'No' }}</td>
                <td>{{ item.days_remaining != null ? item.days_remaining : '—' }}</td>
              </tr>
              <tr v-if="items.length === 0">
                <td colspan="7" style="text-align: center; color: var(--text-secondary)">No subscriptions found</td>
              </tr>
            </tbody>
          </table>

          <div class="pagination">
            <button class="btn btn-sm" :disabled="offset === 0" @click="offset = Math.max(0, offset - limit); load()">← Prev</button>
            <span>{{ total === 0 ? 0 : offset + 1 }}–{{ Math.min(offset + limit, total) }} of {{ total }}</span>
            <button class="btn btn-sm" :disabled="offset + limit >= total" @click="offset += limit; load()">Next →</button>
          </div>
        </div>
      </div>

      <aside v-if="selectedUserId" class="detail-panel">
        <div class="detail-panel-header">
          <h2>Subscription #{{ selectedUserId }}</h2>
          <button class="btn btn-sm" @click="selectedUserId = null">Close</button>
        </div>

        <p v-if="detailLoading" class="loading" style="padding: 1rem">Loading details...</p>
        <p v-if="detailError" class="error-text" style="padding: 0 1rem">{{ detailError }}</p>

        <div v-if="detail && !detailLoading" class="detail-panel-body">
          <div style="text-align: center; margin-bottom: 1rem">
            <span :class="['badge', statusBadgeClass(detail.status)]" style="font-size: 1rem; padding: 0.3rem 1rem">
              {{ statusLabel(detail.status) }}
            </span>
          </div>

          <div class="detail-info-grid">
            <div class="detail-info-row">
              <span class="detail-info-label">User ID</span>
              <span>{{ detail.user_id }}</span>
            </div>
            <div class="detail-info-row">
              <span class="detail-info-label">Telegram ID</span>
              <span>{{ detail.telegram_id }}</span>
            </div>
            <div class="detail-info-row">
              <span class="detail-info-label">Trial until</span>
              <span>{{ detail.trial_expires_at ? formatDate(detail.trial_expires_at) : '—' }}</span>
            </div>
            <div class="detail-info-row">
              <span class="detail-info-label">Subscription until</span>
              <span>{{ detail.subscription_expires_at ? formatDate(detail.subscription_expires_at) : '—' }}</span>
            </div>
            <div class="detail-info-row">
              <span class="detail-info-label">Days remaining</span>
              <span>{{ detail.days_remaining != null ? detail.days_remaining : '—' }}</span>
            </div>
            <div class="detail-info-row">
              <span class="detail-info-label">Exempt</span>
              <span>{{ detail.is_exempt ? 'Yes' : 'No' }}</span>
            </div>
          </div>

          <div class="detail-actions">
            <button
              v-if="!detail.is_exempt"
              class="btn btn-primary btn-sm"
              :disabled="actionLoading"
              @click="handleExempt"
            >
              Grant free access
            </button>
            <button
              v-if="detail.is_exempt"
              class="btn btn-sm btn-danger-outline"
              :disabled="actionLoading"
              @click="handleRemoveExempt"
            >
              Remove free access
            </button>
            <button
              v-if="!detail.is_exempt"
              class="btn btn-sm"
              :disabled="actionLoading"
              @click="handleResetTrial"
            >
              Reset trial
            </button>
          </div>

          <p v-if="actionError" class="error-text" style="margin-top: 0.5rem">{{ actionError }}</p>
          <p v-if="actionSuccess" class="success-text" style="margin-top: 0.5rem">{{ actionSuccess }}</p>

          <div v-if="detail.payment_history && detail.payment_history.length > 0" style="margin-top: 1.5rem">
            <h3 style="font-size: 0.9rem; margin-bottom: 0.5rem; color: var(--text-secondary); text-transform: uppercase; font-weight: 600">Payment history</h3>
            <table class="data-table" style="font-size: 0.8rem">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Plan</th>
                  <th>Stars</th>
                  <th>Charge ID</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="payment in detail.payment_history" :key="payment.id">
                  <td style="white-space: nowrap">{{ formatDate(payment.created_at) }}</td>
                  <td><span class="badge badge-info">{{ payment.plan }}</span></td>
                  <td>{{ payment.stars_amount }}</td>
                  <td style="max-width: 140px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: monospace; font-size: 0.75rem" :title="payment.telegram_payment_charge_id">
                    {{ payment.telegram_payment_charge_id }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else-if="detail.payment_history && detail.payment_history.length === 0" style="margin-top: 1.5rem">
            <p style="color: var(--text-secondary); font-size: 0.85rem">No payment history</p>
          </div>
        </div>
      </aside>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSubscriptions, getUserSubscription, exemptUser, removeExempt, resetTrial } from '../api/admin'
import type { SubscriptionListItem, SubscriptionDetail, SubscriptionStatus } from '../types'

const items = ref<SubscriptionListItem[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const limit = 20
const offset = ref(0)
const statusFilter = ref('')

const selectedUserId = ref<number | null>(null)
const detail = ref<SubscriptionDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')

const actionLoading = ref(false)
const actionError = ref('')
const actionSuccess = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const params: { limit: number; offset: number; status?: string } = { limit, offset: offset.value }
    if (statusFilter.value) params.status = statusFilter.value
    const res = await getSubscriptions(params)
    items.value = res.items
    total.value = res.total
  } catch (e: any) {
    error.value = e.response?.data?.message || 'Failed to load subscriptions'
  } finally {
    loading.value = false
  }
}

async function selectUser(userId: number) {
  if (selectedUserId.value === userId) {
    selectedUserId.value = null
    detail.value = null
    return
  }
  selectedUserId.value = userId
  detailLoading.value = true
  detailError.value = ''
  detail.value = null
  actionError.value = ''
  actionSuccess.value = ''
  try {
    detail.value = await getUserSubscription(userId)
  } catch (e: any) {
    detailError.value = e.response?.data?.message || 'Failed to load subscription details'
  } finally {
    detailLoading.value = false
  }
}

async function handleExempt() {
  if (!selectedUserId.value) return
  if (!confirm('Grant free access to this user?')) return
  await performAction(() => exemptUser(selectedUserId.value!), 'Free access granted')
}

async function handleRemoveExempt() {
  if (!selectedUserId.value) return
  if (!confirm('Remove free access from this user?')) return
  await performAction(() => removeExempt(selectedUserId.value!), 'Free access removed')
}

async function handleResetTrial() {
  if (!selectedUserId.value) return
  if (!confirm('Reset trial for this user?')) return
  await performAction(() => resetTrial(selectedUserId.value!), 'Trial reset successfully')
}

async function performAction(action: () => Promise<void>, successMessage: string) {
  actionLoading.value = true
  actionError.value = ''
  actionSuccess.value = ''
  try {
    await action()
    actionSuccess.value = successMessage
    if (selectedUserId.value) {
      detail.value = await getUserSubscription(selectedUserId.value)
    }
    await load()
  } catch (e: any) {
    actionError.value = e.response?.data?.message || 'Action failed'
  } finally {
    actionLoading.value = false
  }
}

function statusBadgeClass(status: SubscriptionStatus): string {
  switch (status) {
    case 'not_started': return 'badge-gray'
    case 'trial': return 'badge-info'
    case 'active': return 'badge-success'
    case 'grace_period': return 'badge-warning'
    case 'expired': return 'badge-danger'
    case 'exempt': return 'badge-exempt'
    default: return 'badge-gray'
  }
}

function statusLabel(status: SubscriptionStatus): string {
  switch (status) {
    case 'not_started': return 'Not started'
    case 'trial': return 'Trial'
    case 'active': return 'Active'
    case 'grace_period': return 'Grace period'
    case 'expired': return 'Expired'
    case 'exempt': return 'Exempt'
    default: return status
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

onMounted(load)
</script>

<style scoped>
.subscriptions-layout {
  display: flex;
  gap: 1rem;
}

.subscriptions-table-wrapper {
  flex: 1;
  min-width: 0;
}

.row-selected td {
  background: #eef2ff !important;
}

.detail-panel {
  width: 380px;
  flex-shrink: 0;
  background: var(--card-bg);
  border: 1px solid var(--border);
  border-radius: 8px;
  align-self: flex-start;
  position: sticky;
  top: 1.5rem;
}

.detail-panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 1.2rem;
  border-bottom: 1px solid var(--border);
}

.detail-panel-header h2 {
  font-size: 1rem;
  margin: 0;
}

.detail-panel-body {
  padding: 1.2rem;
}

.detail-info-grid {
  display: grid;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.detail-info-row {
  display: flex;
  justify-content: space-between;
  padding: 0.3rem 0;
  border-bottom: 1px solid #f3f4f6;
  font-size: 0.85rem;
}

.detail-info-label {
  color: var(--text-secondary);
}

.detail-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.btn-danger-outline {
  border: 1px solid var(--danger);
  color: var(--danger);
  background: transparent;
}

.btn-danger-outline:hover {
  background: #fef2f2;
}

.badge-exempt {
  background: #ede9fe;
  color: #5b21b6;
}

.success-text {
  color: var(--success);
  font-size: 0.85rem;
}
</style>
