<template>
  <div>
    <h1 class="page-title">Дашборд</h1>
    <p v-if="loading" class="loading">Загрузка метрик...</p>
    <p v-if="error" class="error-text">{{ error }}</p>

    <div v-if="metrics" class="metric-grid">
      <div class="metric-card">
        <div class="value">{{ metrics.dau }}</div>
        <div class="label">Активных сегодня</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ metrics.wau }}</div>
        <div class="label">Активных за 7 дней</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ metrics.mau }}</div>
        <div class="label">Активных за 30 дней</div>
      </div>
      <div class="metric-card">
        <div class="value">{{ formatPercent(metrics.reminder_conversion.rate) }}</div>
        <div class="label">Написали после напоминания ({{ metrics.reminder_conversion.converted }}/{{ metrics.reminder_conversion.total }})</div>
      </div>
    </div>

    <p v-if="metrics" style="color: var(--text-secondary); font-size: 0.85rem">
      Обновлено: {{ formatDate(metrics.calculated_at) }}
      — обновляется каждые 15 мин
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
