<script setup lang="ts">
import { onMounted, onUnmounted, computed, ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from './stores/auth'
import { useSettingsStore } from './stores/settings'
import { useTelegram } from './composables/useTelegram'
import AppIcon from './components/AppIcon.vue'
import WisdomBanner from './components/WisdomBanner.vue'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const { isInTelegram, getInitData, getDetectedTimezone, getThemeParams, getColorScheme } = useTelegram()

const wisdomPhase = ref<'pending' | 'showing' | 'done'>('pending')

const isLoading = computed(() => authStore.loading)
const authError = computed(() => authStore.error)
const isAuthenticated = computed(() => authStore.isAuthenticated)

const tabs = [
  { path: '/today', label: 'Сегодня', icon: 'today' },
  { path: '/week', label: 'Неделя', icon: 'week' },
  { path: '/month', label: 'Месяц', icon: 'month' },
  { path: '/year', label: 'Год', icon: 'year' },
  { path: '/info', label: 'О проекте', icon: 'info' },
  { path: '/settings', label: 'Настройки', icon: 'settings' },
]

const currentTab = computed(() => route.path)
const wisdomBannerRef = ref<InstanceType<typeof WisdomBanner> | null>(null)
const pendingNavPath = ref<string | null>(null)

function navigateTab(path: string) {
  if (wisdomPhase.value === 'showing') {
    pendingNavPath.value = path
    wisdomBannerRef.value?.dismiss()
    return
  }
  router.push(path)
}

function onWisdomHidden() {
  wisdomPhase.value = 'done'
  if (pendingNavPath.value) {
    router.push(pendingNavPath.value)
    pendingNavPath.value = null
  }
}

function reload() {
  window.location.reload()
}

function applyTheme() {
  const theme = getThemeParams()
  const scheme = getColorScheme()
  const root = document.documentElement

  // Toggle dark class — add or remove based on current scheme
  if (scheme === 'dark') {
    root.classList.add('dark')
  } else {
    root.classList.remove('dark')
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

  // Subscribe to Telegram theme changes
  if (window.Telegram?.WebApp) {
    window.Telegram.WebApp.onEvent('themeChanged', applyTheme)
  }

  const initData = getInitData()
  if (initData) {
    const timezone = getDetectedTimezone()
    try {
      await authStore.authenticate(initData, timezone)
      await settingsStore.fetchSettings()
    } catch {
      // Error handled in store
    }
  } else if (!isInTelegram.value) {
    console.warn('Not running inside Telegram — dev auth bypass')
    try {
      const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone
      await authStore.authenticateDev(timezone)
      console.info('Dev auth successful')
      await settingsStore.fetchSettings()
    } catch {
      // Error handled in store
    }
  }

  // Determine wisdom phase after settings are loaded
  if (settingsStore.settings?.show_wisdom !== false) {
    wisdomPhase.value = 'showing'
  } else {
    wisdomPhase.value = 'done'
  }
})

onUnmounted(() => {
  if (window.Telegram?.WebApp) {
    window.Telegram.WebApp.offEvent('themeChanged', applyTheme)
  }
})
</script>

<template>
  <div class="app">
    <!-- Loading state -->
    <div v-if="isLoading" class="auth-loading">
      <div class="spinner"></div>
      <p class="auth-loading__text">Авторизация...</p>
    </div>

    <!-- Auth error -->
    <div v-else-if="authError && !isAuthenticated" class="auth-error">
      <AppIcon name="alert-circle" :size="40" class="auth-error__icon" />
      <p class="auth-error__text">{{ authError }}</p>
      <button class="auth-error__btn" @click="reload">Повторить</button>
    </div>

    <!-- Main app -->
    <template v-else>
      <main class="content">
        <WisdomBanner
          v-if="wisdomPhase === 'showing'"
          ref="wisdomBannerRef"
          @hidden="onWisdomHidden"
        />
        <router-view v-if="wisdomPhase === 'done'" v-slot="{ Component, route: viewRoute }">
          <Transition name="view" mode="out-in">
            <div :key="viewRoute.path">
              <component :is="Component" />
            </div>
          </Transition>
        </router-view>
      </main>

      <!-- Bottom tabs -->
      <nav class="bottom-tabs">
        <button
          v-for="tab in tabs"
          :key="tab.path"
          :class="['tab', { 'tab--active': currentTab === tab.path }]"
          @click="navigateTab(tab.path)"
        >
          <span class="tab__icon">
            <AppIcon :name="tab.icon" :size="22" />
          </span>
          <span class="tab__label">{{ tab.label }}</span>
          <span v-if="currentTab === tab.path" class="tab__indicator" />
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
  padding-bottom: calc(56px + 16px + var(--dt-safe-bottom));
  overflow-y: auto;
}

/* Bottom tabs */
.bottom-tabs {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  display: flex;
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border-top: 1px solid var(--dt-card-border, rgba(0,0,0,0.06));
  padding: 4px 0 calc(6px + var(--dt-safe-bottom));
  z-index: 100;
}

.tab {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  position: relative;
  padding: 5px 2px 2px;
  background: none;
  border: none;
  color: var(--tg-hint-color, #999);
  font-size: 10px;
  cursor: pointer;
  transition: color 200ms ease;
  -webkit-tap-highlight-color: transparent;
}

.tab--active {
  color: var(--tg-link-color, #2481cc);
}

.tab__icon {
  line-height: 0;
  transition: transform 200ms ease;
}

.tab--active .tab__icon {
  transform: scale(1.05);
}

.tab__label {
  margin-top: 2px;
  font-weight: 500;
  letter-spacing: 0.01em;
}

.tab__indicator {
  position: absolute;
  bottom: 0;
  width: 18px;
  height: 2.5px;
  background: var(--tg-link-color, #2481cc);
  border-radius: 2px;
  animation: dt-indicator-in 0.2s ease;
}

@keyframes dt-indicator-in {
  from { transform: scaleX(0); opacity: 0; }
  to { transform: scaleX(1); opacity: 1; }
}

/* Auth states */
.auth-loading,
.auth-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  gap: 16px;
}

.auth-loading__text {
  color: var(--tg-hint-color, #999);
  font-size: 14px;
}

.spinner {
  width: 32px;
  height: 32px;
  border: 2.5px solid var(--dt-card-border, rgba(0,0,0,0.08));
  border-top-color: var(--tg-link-color, #2481cc);
  border-radius: 50%;
  animation: dt-spin 0.7s linear infinite;
}

@keyframes dt-spin {
  to { transform: rotate(360deg); }
}

.auth-error__icon {
  color: var(--dt-error-text, #e53935);
  opacity: 0.7;
}

.auth-error__text {
  color: var(--tg-text-color, #000);
  font-size: 14px;
  text-align: center;
  padding: 0 24px;
}

.auth-error__btn {
  padding: 10px 28px;
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  border: none;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 200ms ease;
}

.auth-error__btn:hover {
  filter: brightness(1.08);
}

.auth-error__btn:active {
  transform: scale(0.97);
}
</style>
