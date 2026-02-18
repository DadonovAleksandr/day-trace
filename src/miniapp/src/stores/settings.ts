import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { UserSettings } from '../types'
import { getSettings, updateSettings } from '../api/settings'

export const useSettingsStore = defineStore('settings', () => {
  const settings = ref<UserSettings | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchSettings() {
    loading.value = true
    error.value = null
    try {
      settings.value = await getSettings()
    } catch (err: any) {
      error.value = err.response?.data?.message || 'Failed to load settings'
    } finally {
      loading.value = false
    }
  }

  async function saveSettings(dto: Partial<UserSettings>) {
    loading.value = true
    error.value = null
    try {
      settings.value = await updateSettings(dto)
    } catch (err: any) {
      error.value = err.response?.data?.message || 'Failed to save settings'
      throw err
    } finally {
      loading.value = false
    }
  }

  return { settings, loading, error, fetchSettings, saveSettings }
})
