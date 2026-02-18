// Domain types matching backend API responses

export interface EventItem {
  id: string
  text: string
  importance: number
  local_date: string
  created_at: string
  updated_at?: string
}

export interface UserSettings {
  timezone: string
  reminder_time: string
  reminder_enabled: boolean
  week_end: string
}

export interface Summary {
  id: string
  period_type: string
  period_start: string
  period_end: string
  status: string
  version: number
  content: SummaryContent | null
  last_generated_at: string | null
}

export interface SummaryContent {
  events: SummaryEvent[]
  total_events: number
  period_start: string
  period_end: string
}

export interface SummaryEvent {
  event_id: string
  text: string
  importance: number
  local_date: string
}

export interface PaginatedResponse<T> {
  items: T[]
  next_cursor: string | null
}

export interface PeriodJobResult {
  job_id: string
  period_type: string
  period_start: string
  period_end: string
  run_number: number
  status: string
  summary_id: string
}

export interface AuthResponse {
  token: string
  user_id: number
  is_new: boolean
}

export interface ApiError {
  error: string
  message: string
}
