import apiClient from './client'
import type { PaginatedResponse, Summary } from '../types'

export async function getSummaries(
  periodType: string,
  params?: {
    from?: string
    to?: string
    limit?: number
    cursor?: string
  }
): Promise<PaginatedResponse<Summary>> {
  const { data } = await apiClient.get(`/summaries/${periodType}`, { params })
  return data
}

export async function setHighlight(
  periodType: string,
  params: {
    event_id: string
    period_start: string
    period_end: string
  }
): Promise<Summary> {
  const { data } = await apiClient.put(`/summaries/${periodType}/highlight`, params)
  return data
}
