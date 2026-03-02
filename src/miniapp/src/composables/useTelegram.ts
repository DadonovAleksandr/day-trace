import { ref, onMounted } from 'vue'

// Telegram Mini App WebApp interface
declare global {
  interface Window {
    Telegram?: {
      WebApp: TelegramWebApp
    }
  }
}

interface SafeAreaInset {
  top: number
  bottom: number
  left: number
  right: number
}

export type HapticImpactStyle = 'light' | 'medium' | 'heavy' | 'rigid' | 'soft'
export type HapticNotificationType = 'error' | 'success' | 'warning'

interface TelegramHapticFeedback {
  impactOccurred: (style: HapticImpactStyle) => void
  notificationOccurred: (type: HapticNotificationType) => void
  selectionChanged: () => void
}

interface TelegramWebApp {
  initData: string
  initDataUnsafe: {
    user?: {
      id: number
      first_name: string
      last_name?: string
      username?: string
      language_code?: string
    }
    auth_date: number
    hash: string
  }
  themeParams: {
    bg_color?: string
    text_color?: string
    hint_color?: string
    link_color?: string
    button_color?: string
    button_text_color?: string
    secondary_bg_color?: string
  }
  colorScheme: 'light' | 'dark'
  viewportHeight: number
  viewportStableHeight: number
  isExpanded: boolean
  safeAreaInset: SafeAreaInset
  contentSafeAreaInset: SafeAreaInset
  ready: () => void
  expand: () => void
  close: () => void
  onEvent: (eventType: string, callback: (...args: unknown[]) => void) => void
  offEvent: (eventType: string, callback: (...args: unknown[]) => void) => void
  HapticFeedback: TelegramHapticFeedback
  BackButton: {
    isVisible: boolean
    show: () => void
    hide: () => void
    onClick: (callback: () => void) => void
    offClick: (callback: () => void) => void
  }
  MainButton: {
    text: string
    color: string
    textColor: string
    isVisible: boolean
    isActive: boolean
    isProgressVisible: boolean
    show: () => void
    hide: () => void
    enable: () => void
    disable: () => void
    showProgress: (leaveActive?: boolean) => void
    hideProgress: () => void
    onClick: (callback: () => void) => void
    offClick: (callback: () => void) => void
    setText: (text: string) => void
  }
  setHeaderColor: (color: string) => void
  setBackgroundColor: (color: string) => void
  openInvoice: (url: string, callback?: (status: string) => void) => void
}

export function useTelegram() {
  const webApp = ref<TelegramWebApp | null>(null)
  const isInTelegram = ref(false)

  /** Tracks the currently registered BackButton callback to prevent listener leaks. */
  let currentBackButtonCallback: (() => void) | null = null

  onMounted(() => {
    if (window.Telegram?.WebApp) {
      webApp.value = window.Telegram.WebApp
      isInTelegram.value = true

      // Tell Telegram we're ready
      webApp.value.ready()
      // Expand to full height
      webApp.value.expand()
    }
  })

  function getInitData(): string {
    return webApp.value?.initData || ''
  }

  function getDetectedTimezone(): string {
    try {
      return Intl.DateTimeFormat().resolvedOptions().timeZone
    } catch {
      return 'Europe/Moscow'
    }
  }

  function getThemeParams() {
    return webApp.value?.themeParams || {}
  }

  function getColorScheme(): 'light' | 'dark' {
    return webApp.value?.colorScheme || 'light'
  }

  function getTgWebApp(): TelegramWebApp | null {
    return webApp.value ?? window.Telegram?.WebApp ?? null
  }

  /**
   * Shows the Telegram BackButton and registers a click callback.
   * Safely removes any previously registered callback to prevent listener leaks.
   */
  function showBackButton(callback: () => void) {
    try {
      const tg = getTgWebApp()
      if (!tg?.BackButton) return

      // Remove previous callback if exists to avoid listener leaks
      if (currentBackButtonCallback) {
        tg.BackButton.offClick(currentBackButtonCallback)
      }

      currentBackButtonCallback = callback
      tg.BackButton.onClick(currentBackButtonCallback)
      tg.BackButton.show()
    } catch {
      // BackButton may not be available in dev/test Telegram environments
    }
  }

  /**
   * Hides the Telegram BackButton and removes the registered callback.
   */
  function hideBackButton() {
    try {
      const tg = getTgWebApp()
      if (!tg?.BackButton) return

      if (currentBackButtonCallback) {
        tg.BackButton.offClick(currentBackButtonCallback)
        currentBackButtonCallback = null
      }

      tg.BackButton.hide()
    } catch {
      // BackButton may not be available in dev/test Telegram environments
    }
  }

  return {
    webApp,
    isInTelegram,
    getInitData,
    getDetectedTimezone,
    getThemeParams,
    getColorScheme,
    showBackButton,
    hideBackButton,
  }
}
