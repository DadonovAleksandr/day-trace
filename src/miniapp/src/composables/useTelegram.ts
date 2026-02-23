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
}

export function useTelegram() {
  const webApp = ref<TelegramWebApp | null>(null)
  const isInTelegram = ref(false)

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
      return 'UTC'
    }
  }

  function getThemeParams() {
    return webApp.value?.themeParams || {}
  }

  function getColorScheme(): 'light' | 'dark' {
    return webApp.value?.colorScheme || 'light'
  }

  function showBackButton(callback: () => void) {
    if (webApp.value) {
      webApp.value.BackButton.onClick(callback)
      webApp.value.BackButton.show()
    }
  }

  function hideBackButton() {
    webApp.value?.BackButton.hide()
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
