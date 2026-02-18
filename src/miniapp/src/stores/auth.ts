import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authenticateTelegram } from '../api/auth'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(null)
  const userId = ref<number | null>(null)
  const isNew = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const isAuthenticated = computed(() => !!token.value)

  async function authenticate(initData: string, timezone?: string) {
    loading.value = true
    error.value = null
    try {
      const response = await authenticateTelegram(initData, timezone)
      token.value = response.token
      userId.value = response.user_id
      isNew.value = response.is_new
    } catch (err: any) {
      error.value = err.response?.data?.message || err.message || 'Authentication failed'
      throw err
    } finally {
      loading.value = false
    }
  }

  function clearAuth() {
    token.value = null
    userId.value = null
    isNew.value = false
  }

  return {
    token,
    userId,
    isNew,
    loading,
    error,
    isAuthenticated,
    authenticate,
    clearAuth,
  }
})
