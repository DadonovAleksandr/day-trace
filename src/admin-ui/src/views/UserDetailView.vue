<template>
  <div>
    <h1 class="page-title">User Detail</h1>
    <router-link to="/users" class="btn btn-sm" style="margin-bottom: 1rem; display: inline-block">← Back to Users</router-link>

    <p v-if="loading" class="loading">Loading...</p>
    <p v-if="error" class="error-text">{{ error }}</p>

    <div v-if="user" class="card">
      <table class="data-table" style="max-width: 500px">
        <tbody>
          <tr><td><strong>ID</strong></td><td>{{ user.id }}</td></tr>
          <tr><td><strong>Telegram ID</strong></td><td>{{ user.telegram_user_id }}</td></tr>
          <tr>
            <td><strong>Status</strong></td>
            <td>
              <span :class="['badge', user.status === 'active' ? 'badge-success' : 'badge-danger']">
                {{ user.status }}
              </span>
            </td>
          </tr>
          <tr><td><strong>Created</strong></td><td>{{ formatDate(user.created_at) }}</td></tr>
          <tr><td><strong>Timezone</strong></td><td>{{ user.settings?.timezone || '—' }}</td></tr>
          <tr><td><strong>Reminder Time</strong></td><td>{{ user.settings?.reminder_time || '—' }}</td></tr>
          <tr><td><strong>Reminder Enabled</strong></td><td>{{ user.settings?.reminder_enabled ? 'Yes' : 'No' }}</td></tr>
          <tr><td><strong>Week End</strong></td><td>{{ user.settings?.week_end || '—' }}</td></tr>
          <tr><td><strong>Events</strong></td><td>{{ user.event_count }}</td></tr>
          <tr><td><strong>Summaries</strong></td><td>{{ user.summary_count }}</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getUser } from '../api/admin'
import type { UserDetail } from '../types'

const route = useRoute()
const user = ref<UserDetail | null>(null)
const loading = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    const id = Number(route.params.id)
    user.value = await getUser(id)
  } catch (e: any) {
    error.value = e.response?.data?.message || 'Failed to load user'
  } finally {
    loading.value = false
  }
}

function formatDate(iso: string) { return new Date(iso).toLocaleString() }

onMounted(load)
</script>
