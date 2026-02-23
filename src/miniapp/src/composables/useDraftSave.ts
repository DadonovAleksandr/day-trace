import { ref, computed, watch, onUnmounted, type Ref } from 'vue'

const STORAGE_PREFIX = 'dt_draft_'
const DEBOUNCE_MS = 500

function storageKey(dateStr: string): string {
  return `${STORAGE_PREFIX}${dateStr}`
}

function loadDraft(dateStr: string): string {
  try {
    return localStorage.getItem(storageKey(dateStr)) ?? ''
  } catch {
    return ''
  }
}

function saveDraft(dateStr: string, text: string): void {
  try {
    if (text) {
      localStorage.setItem(storageKey(dateStr), text)
    } else {
      localStorage.removeItem(storageKey(dateStr))
    }
  } catch {
    // localStorage may be unavailable (e.g. quota exceeded)
  }
}

export function useDraftSave(dateStr: Ref<string>) {
  const draftText = ref(loadDraft(dateStr.value))

  const hasDraft = computed(() => draftText.value.trim().length > 0)

  let debounceTimer: ReturnType<typeof setTimeout> | null = null

  function scheduleWrite() {
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
    }
    debounceTimer = setTimeout(() => {
      saveDraft(dateStr.value, draftText.value)
      debounceTimer = null
    }, DEBOUNCE_MS)
  }

  // Watch text changes and debounce write to localStorage
  const stopTextWatch = watch(draftText, () => {
    scheduleWrite()
  })

  // When date changes, flush pending write for old date, then load new draft
  const stopDateWatch = watch(dateStr, (newDate, oldDate) => {
    // Flush pending debounce for the old date
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
      debounceTimer = null
      saveDraft(oldDate, draftText.value)
    }
    // Load draft for the new date
    draftText.value = loadDraft(newDate)
  })

  function clearDraft() {
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
    draftText.value = ''
    try {
      localStorage.removeItem(storageKey(dateStr.value))
    } catch {
      // ignore
    }
  }

  onUnmounted(() => {
    // Flush any pending write before unmount
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
      saveDraft(dateStr.value, draftText.value)
      debounceTimer = null
    }
    stopTextWatch()
    stopDateWatch()
  })

  return {
    draftText,
    hasDraft,
    clearDraft,
  }
}
