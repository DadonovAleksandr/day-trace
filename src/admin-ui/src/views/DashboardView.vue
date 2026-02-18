<template>
  <div>
    <h1 class="page-title">Dashboard</h1>
    <p v-if="loading" class="loading">Loading metrics...</p>
    <p v-if="error" class="error-text">{{ error }}</p>

    <div v-if="metrics" class="metric-grid">
      <div class="metric-card">
        <div class="value">{{ metrics.dau }}</div>
        <div class="label">DAU (today)</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ metrics.wau }}</div>
        <div class="label">WAU (7 days)</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ metrics.mau }}</div>
        <div class="label">MAU (30 days)</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ formatPercent(metrics.reminder_conversion.rate) }}</div>
        <div class="label">Reminder→Event ({{ metrics.reminder_conversion.converted }}/{{ metrics.reminder_conversion.total }})</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ formatPercent(metrics.prompt_conversion.rate) }}</div>
        <div class="label">Prompt→Summary ({{ metrics.prompt_conversion.converted }}/{{ metrics.prompt_conversion.total }})</div>
      </div>
    </div>

    <p v-if="metrics" style="color: var(--text-secondary); font-size: 0.85rem">
      Last calculated: {{ formatDate(metrics.calculated_at) }}
      — Auto-refreshes every 15 min
    </p>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { getDashboard } from '../api/admin'
import type { DashboardMetrics } from '../types'

const metrics = ref<DashboardMetrics | null>(null)
const loading = ref(false)
const error = ref('')
let refreshInterval: ReturnType<typeof setInterval> | null = null

async function loadMetrics() {
  loading.value = !metrics.value
  error.value = ''
  try {
    metrics.value = await getDashboard()
  } catch (e: any) {
    error.value = e.response?.data?.message || 'Failed to load metrics'
  } finally {
    loading.value = false
  }
}

function formatPercent(rate: number): string {
  return (rate * 100).toFixed(1) + '%'
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString()
}

onMounted(() => {
  loadMetrics()
  refreshInterval = setInterval(loadMetrics, 15 * 60 * 1000) // 15 min
})

onUnmounted(() => {
  if (refreshInterval) clearInterval(refreshInterval)
})
</script>
