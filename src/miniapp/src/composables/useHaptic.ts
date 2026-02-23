import type { HapticImpactStyle, HapticNotificationType } from './useTelegram'

/**
 * Composable for Telegram Mini App haptic feedback.
 * Provides graceful noop when running outside of Telegram
 * (e.g. in a regular browser during development).
 */
export function useHaptic() {
  function getHapticFeedback() {
    return window.Telegram?.WebApp?.HapticFeedback ?? null
  }

  /**
   * Triggers an impact haptic event.
   * @param style - Impact intensity: 'light' | 'medium' | 'heavy' | 'rigid' | 'soft'
   */
  function impact(style: HapticImpactStyle = 'medium') {
    getHapticFeedback()?.impactOccurred(style)
  }

  /**
   * Triggers a notification haptic event.
   * @param type - Notification type: 'error' | 'success' | 'warning'
   */
  function notification(type: HapticNotificationType) {
    getHapticFeedback()?.notificationOccurred(type)
  }

  /**
   * Triggers a selection change haptic event.
   * Typically used when the user changes a selection (e.g. picker, toggle).
   */
  function selectionChanged() {
    getHapticFeedback()?.selectionChanged()
  }

  return {
    impact,
    notification,
    selectionChanged,
  }
}
