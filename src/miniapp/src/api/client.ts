import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '../stores/auth'
import { v4 as uuidv4 } from 'uuid'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'

const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
})

// Request interceptor: inject Bearer token + X-Client-Operation-Id for mutations
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const authStore = useAuthStore()
    if (authStore.token) {
      config.headers.Authorization = `Bearer ${authStore.token}`
    }

    // Add operation ID for POST, PATCH, DELETE (dedupe)
    const method = config.method?.toUpperCase()
    if (method === 'POST' || method === 'PATCH' || method === 'DELETE') {
      config.headers['X-Client-Operation-Id'] = uuidv4()
    }

    return config
  },
  (error) => Promise.reject(error)
)

// Response interceptor: handle 401 (unauthorized)
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      const authStore = useAuthStore()
      authStore.clearAuth()
    }
    return Promise.reject(error)
  }
)

export default apiClient
