import axios, { type AxiosInstance } from 'axios'
import { useAuthStore } from '../stores/auth'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'

const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
})

const MUTATION_METHODS = ['post', 'patch', 'put', 'delete']

apiClient.interceptors.request.use((config) => {
  if (config.method && MUTATION_METHODS.includes(config.method)) {
    config.headers['X-Client-Operation-Id'] = crypto.randomUUID()
  }
  return config
})

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      const authStore = useAuthStore()
      authStore.logout()
    }
    return Promise.reject(error)
  }
)

export default apiClient
