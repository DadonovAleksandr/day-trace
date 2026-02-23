import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as apiLogin, getSessionInfo } from '../api/admin'

type SessionStatus = 'unknown' | 'authenticated' | 'unauthenticated'

type SessionPayload = {
  email: string
  role: string
}

export const useAuthStore = defineStore('auth', () => {
  const sessionStatus = ref<SessionStatus>('unknown')
  const role = ref<string>('')
  const email = ref<string>('')
  const isRestoring = ref(false)
  let restorePromise: Promise<void> | null = null

  const isAuthenticated = computed(() => sessionStatus.value === 'authenticated')
  const isAdmin = computed(() => isAuthenticated.value && role.value === 'admin')
  const isOperator = computed(() => isAuthenticated.value && (role.value === 'operator' || role.value === 'admin'))
  const isAnalyst = computed(() => isAuthenticated.value && !!role.value)

  function setSession(data: SessionPayload) {
    email.value = data.email
    role.value = data.role
    sessionStatus.value = 'authenticated'
  }

  function clearSession() {
    sessionStatus.value = 'unauthenticated'
    role.value = ''
    email.value = ''
  }

  function normalizeSessionData(data: any): SessionPayload | null {
    const directRole = typeof data?.role === 'string' ? data.role : ''
    const directEmail = typeof data?.email === 'string' ? data.email : ''
    if (directRole) {
      return {
        role: directRole,
        email: directEmail,
      }
    }

    const nestedRole = typeof data?.admin?.role === 'string' ? data.admin.role : ''
    const nestedEmail = typeof data?.admin?.email === 'string' ? data.admin.email : ''
    if (nestedRole) {
      return {
        role: nestedRole,
        email: nestedEmail,
      }
    }

    return null
  }

  async function login(emailVal: string, password: string) {
    const data = await apiLogin(emailVal, password)
    const normalized = normalizeSessionData(data)

    if (!normalized) {
      throw new Error('Invalid admin session payload')
    }

    setSession({
      role: normalized.role,
      email: normalized.email || emailVal,
    })
  }

  function logout() {
    clearSession()
  }

  async function restoreSession() {
    if (sessionStatus.value !== 'unknown') return
    if (restorePromise) return restorePromise

    isRestoring.value = true
    restorePromise = (async () => {
      try {
        const data = await getSessionInfo()
        const normalized = normalizeSessionData(data)
        if (normalized) {
          setSession(normalized)
          return
        }
        clearSession()
      } catch {
        clearSession()
      } finally {
        isRestoring.value = false
        restorePromise = null
      }
    })()

    return restorePromise
  }

  return {
    sessionStatus,
    role,
    email,
    isRestoring,
    isAuthenticated,
    isAdmin,
    isOperator,
    isAnalyst,
    login,
    logout,
    restoreSession,
  }
})
