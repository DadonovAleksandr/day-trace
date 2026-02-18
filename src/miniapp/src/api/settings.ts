import apiClient from './client'
import type { UserSettings } from '../types'

export async function getSettings(): Promise<UserSettings> {
  const { data } = await apiClient.get('/settings')
  return data
}

export async function updateSettings(dto: Partial<UserSettings>): Promise<UserSettings> {
  const { data } = await apiClient.put('/settings', dto)
  return data
}
