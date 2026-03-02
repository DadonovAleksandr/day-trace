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
  show_wisdom: boolean
  wisdom_duration: number
  importance_enabled: boolean
  satisfaction_enabled: boolean
}

export interface Summary {
  id: string
  period_type: string
  period_start: string
  period_end: string
  status: string
  version: number
  content: SummaryContent | null
  highlight_event_id: string | null
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

export interface AuthResponse {
  token: string
  user_id: number
  is_new: boolean
}

export interface WisdomResponse {
  id: number
  text: string
  category: string
  author: string | null
}

export type SubscriptionStatus = 'not_started' | 'trial' | 'active' | 'grace_period' | 'expired' | 'exempt'

export interface SubscriptionInfo {
  status: SubscriptionStatus
  trial_expires_at: string | null
  subscription_expires_at: string | null
  days_remaining: number | null
  is_exempt: boolean
}
