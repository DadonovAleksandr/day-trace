import apiClient from './client'
import type { EventItem, PaginatedResponse } from '../types'

export interface CreateEventDto {
  text: string
  importance: number
  local_date?: string
}

export interface UpdateEventDto {
  text?: string
  importance?: number
}

export async function getEvents(params?: {
  from?: string
  to?: string
  limit?: number
  cursor?: string
}): Promise<PaginatedResponse<EventItem>> {
  const { data } = await apiClient.get('/events', { params })
  return data
}

export async function createEvent(dto: CreateEventDto): Promise<EventItem> {
  const { data } = await apiClient.post('/events', dto)
  return data
}

export async function updateEvent(id: string, dto: UpdateEventDto): Promise<EventItem> {
  const { data } = await apiClient.patch(`/events/${id}`, dto)
  return data
}

export async function deleteEvent(id: string): Promise<void> {
  await apiClient.delete(`/events/${id}`)
}
