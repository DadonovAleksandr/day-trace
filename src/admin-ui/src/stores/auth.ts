import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as apiLogin } from '../api/admin'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem('admin_token'))
  const role = ref<string>(localStorage.getItem('admin_role') || '')
  const email = ref<string>(localStorage.getItem('admin_email') || '')

  const isAuthenticated = computed(() => !!token.value)
  const isAdmin = computed(() => role.value === 'admin')
  const isOperator = computed(() => role.value === 'operator' || isAdmin.value)
  const isAnalyst = computed(() => !!role.value) // all roles can see dashboard

  async function login(emailVal: string, password: string) {
    const data = await apiLogin(emailVal, password)
    token.value = data.token
    role.value = data.role || ''
    email.value = emailVal

    localStorage.setItem('admin_token', data.token)
    localStorage.setItem('admin_role', data.role || '')
    localStorage.setItem('admin_email', emailVal)
  }

  function logout() {
    token.value = null
    role.value = ''
    email.value = ''
    localStorage.removeItem('admin_token')
    localStorage.removeItem('admin_role')
    localStorage.removeItem('admin_email')
  }

  return { token, role, email, isAuthenticated, isAdmin, isOperator, isAnalyst, login, logout }
})
