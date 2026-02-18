<template>
  <div>
    <h1 class="page-title">Users</h1>

    <div class="filters">
      <input v-model="search" placeholder="Search by Telegram ID..." @keyup.enter="load" />
      <select v-model="statusFilter" @change="load">
        <option value="">All statuses</option>
        <option value="active">Active</option>
        <option value="deleted">Deleted</option>
      </select>
      <button class="btn btn-primary btn-sm" @click="load">Search</button>
    </div>

    <p v-if="loading" class="loading">Loading...</p>
    <p v-if="error" class="error-text">{{ error }}</p>

    <div v-if="!loading" class="card">
      <table class="data-table">
        <thead>
          <tr>
            <th>ID</th>
            <th>Telegram ID</th>
            <th>Status</th>
            <th>Timezone</th>
            <th>Reminder</th>
            <th>Created</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in users" :key="user.id">
            <td>{{ user.id }}</td>
            <td>{{ user.telegram_user_id }}</td>
            <td>
              <span :class="['badge', user.status === 'active' ? 'badge-success' : 'badge-danger']">
                {{ user.status }}
              </span>
            </td>
            <td>{{ user.timezone || '—' }}</td>
            <td>{{ user.reminder_enabled ? user.reminder_time || '✓' : '✗' }}</td>
            <td>{{ formatDate(user.created_at) }}</td>
            <td>
              <router-link :to="`/users/${user.id}`" class="btn btn-sm btn-primary">Detail</router-link>
            </td>
          </tr>
          <tr v-if="users.length === 0">
            <td colspan="7" style="text-align:center; color: var(--text-secondary)">No users found</td>
          </tr>
        </tbody>
      </table>

      <div class="pagination">
        <button class="btn btn-sm" :disabled="offset === 0" @click="prevPage">← Prev</button>
        <span>{{ offset + 1 }}–{{ Math.min(offset + limit, total) }} of {{ total }}</span>
        <button class="btn btn-sm" :disabled="offset + limit >= total" @click="nextPage">Next →</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getUsers } from '../api/admin'
import type { UserItem } from '../types'

const users = ref<UserItem[]>([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const search = ref('')
const statusFilter = ref('')
const limit = 20
const offset = ref(0)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await getUsers({
      limit,
      offset: offset.value,
      search: search.value || undefined,
      status: statusFilter.value || undefined,
    })
    users.value = res.items
    total.value = res.total
  } catch (e: any) {
    error.value = e.response?.data?.message || 'Failed to load users'
  } finally {
    loading.value = false
  }
}

function prevPage() { offset.value = Math.max(0, offset.value - limit); load() }
function nextPage() { offset.value += limit; load() }
function formatDate(iso: string) { return new Date(iso).toLocaleDateString() }

onMounted(load)
</script>
