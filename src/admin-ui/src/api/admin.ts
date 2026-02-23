import apiClient from './client'
import type {
  DashboardMetrics,
  PaginatedResponse,
  UserItem,
  UserDetail,
  EventItem,
  SummaryItem,
  PeriodJobItem,
  DeliveryAttemptItem,
  AuditLogItem,
  FeedbackItem,
} from '../types'

// Auth
export async function login(email: string, password: string) {
  const res = await apiClient.post('/admin/auth/login', { email, password })
  return res.data
}

// Dashboard
export async function getDashboard(): Promise<DashboardMetrics> {
  const res = await apiClient.get('/admin/metrics/dashboard')
  return res.data
}

// Users
export async function getUsers(params?: {
  limit?: number
  offset?: number
  search?: string
  status?: string
}): Promise<PaginatedResponse<UserItem>> {
  const res = await apiClient.get('/admin/users', { params })
  return res.data
}

export async function getUser(id: number): Promise<UserDetail> {
  const res = await apiClient.get(`/admin/users/${id}`)
  return res.data
}

// Events (content)
export async function getEvents(params?: {
  limit?: number
  offset?: number
  user_id?: number
  from?: string
  to?: string
  importance?: number
}): Promise<PaginatedResponse<EventItem>> {
  const res = await apiClient.get('/admin/events', { params })
  return res.data
}

// Summaries (content)
export async function getSummaries(params?: {
  limit?: number
  offset?: number
  user_id?: number
  period_type?: string
  from?: string
  to?: string
  status?: string
}): Promise<PaginatedResponse<SummaryItem>> {
  const res = await apiClient.get('/admin/summaries', { params })
  return res.data
}

// Period Jobs (operations)
export async function getPeriodJobs(params?: {
  limit?: number
  offset?: number
  status?: string
  user_id?: number
}): Promise<PaginatedResponse<PeriodJobItem>> {
  const res = await apiClient.get('/admin/period-jobs', { params })
  return res.data
}

// Delivery Attempts (operations)
export async function getDeliveryAttempts(params?: {
  limit?: number
  offset?: number
  status?: string
  user_id?: number
  delivery_type?: string
}): Promise<PaginatedResponse<DeliveryAttemptItem>> {
  const res = await apiClient.get('/admin/delivery-attempts', { params })
  return res.data
}

// Feedback
export async function getFeedback(params?: {
  limit?: number
  offset?: number
  user_id?: number
  status?: string
  from?: string
  to?: string
}): Promise<PaginatedResponse<FeedbackItem>> {
  const res = await apiClient.get('/admin/feedback', { params })
  return res.data
}

export async function markFeedbackRead(id: number): Promise<{ id: number; status: string; read_at: string }> {
  const res = await apiClient.patch(`/admin/feedback/${id}/read`)
  return res.data
}

// Audit Logs
export async function getAuditLogs(params?: {
  limit?: number
  offset?: number
  actor_type?: string
  action?: string
  from?: string
  to?: string
}): Promise<PaginatedResponse<AuditLogItem>> {
  const res = await apiClient.get('/admin/audit-logs', { params })
  return res.data
}
