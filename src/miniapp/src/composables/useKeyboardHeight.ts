import { ref, computed, onMounted, onUnmounted } from 'vue'

/**
 * Tracks virtual keyboard visibility and height.
 *
 * Strategy:
 * 1. Inside Telegram: subscribe to `viewportChanged` event,
 *    compare `viewportStableHeight` (full height without keyboard)
 *    with `viewportHeight` (current visible area).
 * 2. Outside Telegram (browser dev mode): use `window.visualViewport`
 *    API with the same stable-vs-current approach.
 *
 * Exposes a CSS custom property `--dt-keyboard-height` on
 * `document.documentElement` so any component/style can react to it.
 */
export function useKeyboardHeight() {
  const keyboardHeight = ref(0)
  const viewportHeight = ref(window.innerHeight)

  /** Keyboard is considered visible when the difference exceeds a small threshold */
  const isKeyboardVisible = computed(() => keyboardHeight.value > 50)

  // ---- Telegram path ----
  const tgWebApp = window.Telegram?.WebApp ?? null
  let tgHandler: ((event: { isStateStable: boolean }) => void) | null = null

  // ---- VisualViewport fallback path ----
  let visualViewportHandler: (() => void) | null = null
  /** Stable height captured once (full viewport without keyboard) */
  let stableHeight = window.innerHeight

  function applyCssProperty(height: number): void {
    document.documentElement.style.setProperty(
      '--dt-keyboard-height',
      `${height}px`,
    )
  }

  function update(currentHeight: number, stable: number): void {
    const diff = Math.max(0, stable - currentHeight)
    keyboardHeight.value = diff
    viewportHeight.value = currentHeight
    applyCssProperty(diff)
  }

  function setupTelegramListener(): void {
    if (!tgWebApp) return

    // Capture initial stable height from Telegram
    stableHeight = tgWebApp.viewportStableHeight

    tgHandler = (event) => {
      // When the viewport change is "stable" (animation finished)
      // Telegram provides the updated stableHeight
      if (event.isStateStable) {
        stableHeight = tgWebApp.viewportStableHeight
      }
      update(tgWebApp.viewportHeight, stableHeight)
    }

    tgWebApp.onEvent(
      'viewportChanged',
      tgHandler as (...args: unknown[]) => void,
    )
  }

  function teardownTelegramListener(): void {
    if (tgWebApp && tgHandler) {
      tgWebApp.offEvent(
        'viewportChanged',
        tgHandler as (...args: unknown[]) => void,
      )
      tgHandler = null
    }
  }

  function setupVisualViewportListener(): void {
    const vv = window.visualViewport
    if (!vv) return

    // Capture initial stable height
    stableHeight = vv.height

    visualViewportHandler = () => {
      const currentHeight = vv.height

      // If viewport grows back to (or beyond) stable height,
      // keyboard closed -- update the stable reference.
      if (currentHeight >= stableHeight) {
        stableHeight = currentHeight
      }

      update(currentHeight, stableHeight)
    }

    vv.addEventListener('resize', visualViewportHandler)
  }

  function teardownVisualViewportListener(): void {
    if (visualViewportHandler) {
      window.visualViewport?.removeEventListener('resize', visualViewportHandler)
      visualViewportHandler = null
    }
  }

  onMounted(() => {
    if (tgWebApp) {
      setupTelegramListener()
    } else {
      setupVisualViewportListener()
    }
  })

  onUnmounted(() => {
    teardownTelegramListener()
    teardownVisualViewportListener()
    // Reset CSS property on cleanup
    applyCssProperty(0)
  })

  return {
    /** Estimated keyboard height in pixels (0 when hidden) */
    keyboardHeight,
    /** True when keyboard is likely visible (height > 50px threshold) */
    isKeyboardVisible,
    /** Current viewport height (reflects keyboard state) */
    viewportHeight,
  }
}
