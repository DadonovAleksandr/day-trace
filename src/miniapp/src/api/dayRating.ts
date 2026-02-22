import apiClient from './client'

export interface DayRatingResponse {
  local_date: string
  rating: number | null
  updated_at: string | null
}

export interface SetDayRatingDto {
  rating: number
  local_date?: string
}

export async function getDayRating(date?: string): Promise<DayRatingResponse> {
  const { data } = await apiClient.get('/day-rating', { params: date ? { date } : {} })
  return data
}

export async function setDayRating(dto: SetDayRatingDto): Promise<DayRatingResponse> {
  const { data } = await apiClient.put('/day-rating', dto)
  return data
}
