<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from './stores/auth'
import { useTelegram } from './composables/useTelegram'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const { isInTelegram, getInitData, getDetectedTimezone, getThemeParams, getColorScheme } = useTelegram()

const isLoading = computed(() => authStore.loading)
const authError = computed(() => authStore.error)
const isAuthenticated = computed(() => authStore.isAuthenticated)

const tabs = [
  { path: '/today', label: 'Сегодня', icon: '📝' },
  { path: '/week', label: 'Неделя', icon: '📅' },
  { path: '/month', label: 'Месяц', icon: '📆' },
  { path: '/year', label: 'Год', icon: '📊' },
  { path: '/settings', label: 'Настройки', icon: '⚙️' },
]

const currentTab = computed(() => route.path)

function reload() {
  window.location.reload()
}

function applyTheme() {
  const theme = getThemeParams()
  const scheme = getColorScheme()
  const root = document.documentElement

  if (scheme === 'dark') {
    root.classList.add('dark')
  }

  if (theme.bg_color) root.style.setProperty('--tg-bg-color', theme.bg_color)
  if (theme.text_color) root.style.setProperty('--tg-text-color', theme.text_color)
  if (theme.hint_color) root.style.setProperty('--tg-hint-color', theme.hint_color)
  if (theme.link_color) root.style.setProperty('--tg-link-color', theme.link_color)
  if (theme.button_color) root.style.setProperty('--tg-button-color', theme.button_color)
  if (theme.button_text_color) root.style.setProperty('--tg-button-text-color', theme.button_text_color)
  if (theme.secondary_bg_color) root.style.setProperty('--tg-secondary-bg-color', theme.secondary_bg_color)
}

onMounted(async () => {
  applyTheme()

  // Auth flow: extract init data → authenticate
  const initData = getInitData()
  if (initData) {
    const timezone = getDetectedTimezone()
    try {
      await authStore.authenticate(initData, timezone)
    } catch {
      // Error handled in store
    }
  } else if (!isInTelegram.value) {
    // Development mode: skip auth
    console.warn('Not running inside Telegram — dev mode')
  }
})
</script>

<template>
  <div class="app">
    <!-- Loading state -->
    <div v-if="isLoading" class="auth-loading">
      <div class="spinner"></div>
      <p>Авторизация...</p>
    </div>

    <!-- Auth error -->
    <div v-else-if="authError && !isAuthenticated" class="auth-error">
      <p>❌ {{ authError }}</p>
      <button @click="reload">Повторить</button>
    </div>

    <!-- Main app -->
    <template v-else>
      <main class="content">
        <router-view />
      </main>

      <!-- Bottom tabs -->
      <nav class="bottom-tabs">
        <button
          v-for="tab in tabs"
          :key="tab.path"
          :class="['tab', { active: currentTab === tab.path }]"
          @click="router.push(tab.path)"
        >
          <span class="tab-icon">{{ tab.icon }}</span>
          <span class="tab-label">{{ tab.label }}</span>
        </button>
      </nav>
    </template>
  </div>
</template>

<style scoped>
.app {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--tg-bg-color, #ffffff);
  color: var(--tg-text-color, #000000);
}

.content {
  flex: 1;
  padding: 16px;
  padding-bottom: 72px; /* space for bottom tabs */
  overflow-y: auto;
}

.bottom-tabs {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  display: flex;
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border-top: 1px solid var(--tg-hint-color, #ccc);
  padding: 4px 0;
  z-index: 100;
}

.tab {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 6px 2px;
  background: none;
  border: none;
  color: var(--tg-hint-color, #999);
  font-size: 10px;
  cursor: pointer;
  transition: color 0.2s;
}

.tab.active {
  color: var(--tg-link-color, #2481cc);
}

.tab-icon {
  font-size: 20px;
  line-height: 1;
}

.tab-label {
  margin-top: 2px;
}

.auth-loading,
.auth-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  gap: 16px;
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid var(--tg-hint-color, #ccc);
  border-top-color: var(--tg-link-color, #2481cc);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.auth-error button {
  padding: 8px 24px;
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  border: none;
  border-radius: 8px;
  font-size: 14px;
  cursor: pointer;
}
</style>
