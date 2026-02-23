export interface AdminUser {
  id: number
  email: string
  role: string
  status: string
  created_at: string
}

export interface DashboardMetrics {
  dau: number
  wau: number
  mau: number
  reminder_conversion: {
    converted: number
    total: number
    rate: number
  }
  prompt_conversion: {
    converted: number
    total: number
    rate: number
  }
  calculated_at: string
}

export interface UserItem {
  id: number
  telegram_user_id: number
  status: string
  created_at: string
  timezone?: string
  reminder_enabled?: boolean
  reminder_time?: string
  week_end?: string
}

export interface UserDetail extends UserItem {
  settings?: {
    timezone: string
    reminder_time: string
    reminder_enabled: boolean
    week_end: string
  }
  event_count: number
  summary_count: number
}

export interface EventItem {
  id: string
  user_id: number
  text: string
  local_date: string
  importance: number
  created_at: string
  updated_at?: string
}

export interface SummaryItem {
  id: number
  user_id: number
  period_type: string
  period_start: string
  period_end: string
  status: string
  version: number
  content?: any
  last_generated_at?: string
}

export interface PeriodJobItem {
  id: number
  idempotency_key: string
  user_id: number
  period_type: string
  period_start: string
  period_end: string
  run_number: number
  status: string
  attempt_count: number
  lease_id?: string
  target_summary_version: number
  started_at?: string
  finished_at?: string
  error?: string
  reconciled_at?: string
  recovery_source?: string
  created_at: string
  is_stuck: boolean
}

export interface DeliveryAttemptItem {
  id: number
  user_id: number
  delivery_type: string
  reference_id?: number
  attempt_number: number
  status: string
  error_message?: string
  telegram_message_id?: number
  scheduled_at?: string
  sent_at?: string
  created_at: string
}

export interface AuditLogItem {
  id: number
  actor_type: string
  actor_id?: string
  action: string
  target_type?: string
  target_id?: string
  payload?: string
  outcome: string
  created_at: string
}

export interface FeedbackItem {
  id: number
  user_id: number
  telegram_user_id?: number
  text: string
  status: string
  created_at: string
  read_at?: string
}

export interface PaginatedResponse<T> {
  items: T[]
  total: number
  limit: number
  offset: number
}
