<template>
  <div>
    <h1 class="page-title">Audit Log</h1>

    <div class="filters">
      <select v-model="filters.actor_type">
        <option value="">All actors</option>
        <option value="admin">Admin</option>
        <option value="system">System</option>
      </select>
      <input v-model="filters.action" placeholder="Action filter..." />
      <input v-model="filters.from" type="date" placeholder="From" />
      <input v-model="filters.to" type="date" placeholder="To" />
      <button class="btn btn-primary btn-sm" @click="load">Search</button>
    </div>

    <p v-if="loading" class="loading">Loading audit logs...</p>
    <p v-if="error" class="error-text">{{ error }}</p>

    <div v-if="!loading" class="card">
      <table class="data-table">
        <thead>
          <tr>
            <th>ID</th>
            <th>Timestamp</th>
            <th>Actor</th>
            <th>Action</th>
            <th>Target</th>
            <th>Outcome</th>
            <th>Payload</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="log in logs" :key="log.id">
            <td>{{ log.id }}</td>
            <td style="white-space: nowrap">{{ formatDate(log.created_at) }}</td>
            <td>
              <span class="badge badge-gray">{{ log.actor_type }}</span>
              <span v-if="log.actor_id" style="margin-left: 4px; font-size: 0.8rem">#{{ log.actor_id }}</span>
            </td>
            <td><strong>{{ log.action }}</strong></td>
            <td>
              <template v-if="log.target_type">
                {{ log.target_type }}<span v-if="log.target_id">#{{ log.target_id }}</span>
              </template>
              <template v-else>—</template>
            </td>
            <td>
              <span :class="['badge', log.outcome === 'success' ? 'badge-success' : 'badge-danger']">
                {{ log.outcome }}
              </span>
            </td>
            <td style="max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.8rem; font-family: monospace" :title="formatPayload(log.payload)">
              {{ formatPayload(log.payload) }}
            </td>
          </tr>
          <tr v-if="logs.length === 0">
            <td colspan="7" style="text-align: center; color: var(--text-secondary)">No audit logs found</td>
          </tr>
        </tbody>
      </table>

      <div class="pagination">
        <button class="btn btn-sm" :disabled="offset === 0" @click="offset = Math.max(0, offset - limit); load()">← Prev</button>
        <span>{{ offset + 1 }}–{{ Math.min(offset + limit, total) }} of {{ total }}</span>
        <button class="btn btn-sm" :disabled="offset + limit >= total" @click="offset += limit; load()">Next →</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getAuditLogs } from '../api/admin'
import type { AuditLogItem } from '../types'

const logs = ref<AuditLogItem[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const limit = 20
const offset = ref(0)
const filters = ref<{ actor_type?: string; action?: string; from?: string; to?: string }>({})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const params: any = { limit, offset: offset.value }
    if (filters.value.actor_type) params.actor_type = filters.value.actor_type
    if (filters.value.action) params.action = filters.value.action
    if (filters.value.from) params.from = filters.value.from
    if (filters.value.to) params.to = filters.value.to
    const res = await getAuditLogs(params)
    logs.value = res.items
    total.value = res.total
  } catch (e: any) {
    error.value = e.response?.data?.message || 'Failed to load audit logs'
  } finally {
    loading.value = false
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

function formatPayload(payload: string | undefined): string {
  if (!payload || payload === '{}') return '—'
  try {
    return typeof payload === 'string' ? payload : JSON.stringify(payload)
  } catch {
    return String(payload)
  }
}

onMounted(load)
</script>
