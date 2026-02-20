import axios from 'axios'
import type { AuthResponse } from '../types'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'

export async function authenticateTelegram(
  initData: string,
  timezone?: string
): Promise<AuthResponse> {
  const { data } = await axios.post(`${API_BASE_URL}/auth/telegram`, {
    init_data: initData,
    timezone,
  })
  return data
}

export async function authenticateDev(
  timezone?: string
): Promise<AuthResponse> {
  const { data } = await axios.post(`${API_BASE_URL}/auth/dev`, {
    timezone,
  })
  return data
}
