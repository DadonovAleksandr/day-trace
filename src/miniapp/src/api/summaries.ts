import apiClient from './client'
import type { PaginatedResponse, Summary, PeriodJobResult } from '../types'

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

export async function runSummary(
  periodType: string,
  params?: {
    period_start?: string
    period_end?: string
  }
): Promise<PeriodJobResult> {
  const { data } = await apiClient.post(`/summaries/${periodType}/run`, params || {})
  return data
}
