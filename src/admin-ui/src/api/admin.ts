import apiClient from './client'
import type {
  AdminSessionInfo,
  AdminBroadcastRequest,
  AdminBroadcastResponse,
  AdminBroadcastCampaignItem,
  AdminFeedbackReadResponse,
  AdminFeedbackReplyRequest,
  AdminFeedbackReplyResponse,
  DashboardMetrics,
  PaginatedResponse,
  UserItem,
  UserDetail,
  EventItem,
  SummaryItem,
  DeliveryAttemptItem,
  AuditLogItem,
  FeedbackItem,
  SubscriptionListItem,
  SubscriptionDetail,
  SubscriptionsListResponse,
} from '../types'

// Auth
export async function login(email: string, password: string) {
  const res = await apiClient.post('/admin/auth/login', { email, password })
  return res.data
}

export async function logout(): Promise<{ message: string }> {
  const res = await apiClient.post('/admin/auth/logout')
  return res.data
}

export async function getSessionInfo(): Promise<AdminSessionInfo> {
  const res = await apiClient.get('/admin/auth/me')
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

type AdminBroadcastRawResponse = Partial<AdminBroadcastResponse> & {
  campaignId?: number
  queuedCount?: number
  total?: number
  sent?: number
  failed?: number
  total_count?: number
  success_count?: number
  failure_count?: number
}

export async function sendAdminBroadcast(payload: AdminBroadcastRequest): Promise<AdminBroadcastResponse> {
  const res = await apiClient.post('/admin/messaging/broadcast', payload)
  const data = res.data as AdminBroadcastRawResponse

  return {
    campaign_id: Number(data.campaign_id ?? data.campaignId ?? 0),
    status: String(data.status ?? 'queued'),
    queued_count: Number(data.queued_count ?? data.queuedCount ?? data.total ?? data.total_count ?? 0),
    audience: data.audience ?? payload.audience,
  }
}

type AdminBroadcastCampaignRaw = Partial<AdminBroadcastCampaignItem> & {
  campaign_id?: number
  campaignId?: number
  createdAt?: string
  queuedAt?: string | null
  completedAt?: string | null
  text_preview?: string
  textPreview?: string
  queuedCount?: number
  pendingCount?: number
  sentCount?: number
  failedCount?: number
  terminalFailedCount?: number
  created_by_admin_user_id?: number
  createdByAdminUserId?: number
  counts?: {
    total?: number
    pending?: number
    sent?: number
    failed?: number
    terminal_failed?: number
    terminalFailed?: number
  }
}

function toOptionalNumber(value: unknown): number | undefined {
  if (value == null || value === '') return undefined
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

function normalizeBroadcastCampaign(item: AdminBroadcastCampaignRaw): AdminBroadcastCampaignItem {
  const counts = item.counts ?? {}

  return {
    id: Number(item.id ?? item.campaign_id ?? item.campaignId ?? 0),
    audience: item.audience ?? 'active',
    status: String(item.status ?? 'queued'),
    created_at: String(item.created_at ?? item.createdAt ?? item.queued_at ?? item.queuedAt ?? ''),
    queued_at: (item.queued_at ?? item.queuedAt) ?? null,
    completed_at: (item.completed_at ?? item.completedAt) ?? null,
    queued_count: toOptionalNumber(item.queued_count ?? item.queuedCount ?? counts.total ?? (item as any).total ?? (item as any).total_count),
    pending_count: toOptionalNumber(item.pending_count ?? item.pendingCount ?? counts.pending),
    sent_count: toOptionalNumber(item.sent_count ?? item.sentCount ?? counts.sent ?? (item as any).success_count ?? (item as any).sent),
    failed_count: toOptionalNumber(item.failed_count ?? item.failedCount ?? counts.failed ?? (item as any).failure_count ?? (item as any).failed),
    terminal_failed_count: toOptionalNumber(item.terminal_failed_count ?? item.terminalFailedCount ?? counts.terminal_failed ?? counts.terminalFailed),
    created_by_admin_id: toOptionalNumber(
      item.created_by_admin_id ??
      item.created_by_admin_user_id ??
      (item as any).createdByAdminId ??
      item.createdByAdminUserId
    ),
    text: typeof item.text === 'string'
      ? item.text
      : typeof item.text_preview === 'string'
        ? item.text_preview
        : typeof item.textPreview === 'string'
          ? item.textPreview
          : undefined,
  }
}

type AdminBroadcastCampaignListRaw =
  | AdminBroadcastCampaignRaw[]
  | {
      items?: AdminBroadcastCampaignRaw[]
      campaigns?: AdminBroadcastCampaignRaw[]
      total?: number
      count?: number
      limit?: number
      offset?: number
    }

export async function getAdminBroadcastCampaigns(params?: {
  limit?: number
  offset?: number
  status?: string
}): Promise<PaginatedResponse<AdminBroadcastCampaignItem>> {
  const res = await apiClient.get('/admin/messaging/broadcasts', { params })
  const data = (res.data ?? {}) as AdminBroadcastCampaignListRaw

  const items = Array.isArray(data)
    ? data
    : Array.isArray(data.items)
      ? data.items
      : Array.isArray(data.campaigns)
        ? data.campaigns
        : []

  return {
    items: items.map(normalizeBroadcastCampaign),
    total: Number(Array.isArray(data) ? items.length : data.total ?? data.count ?? items.length),
    limit: Number(Array.isArray(data) ? (params?.limit ?? items.length) : data.limit ?? params?.limit ?? items.length),
    offset: Number(Array.isArray(data) ? (params?.offset ?? 0) : data.offset ?? params?.offset ?? 0),
  }
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

export async function markFeedbackRead(id: number): Promise<AdminFeedbackReadResponse> {
  const res = await apiClient.patch(`/admin/feedback/${id}/read`)
  return res.data
}

export async function replyToFeedback(id: number, payload: AdminFeedbackReplyRequest): Promise<AdminFeedbackReplyResponse> {
  const res = await apiClient.post(`/admin/feedback/${id}/reply`, payload)
  return res.data
}

// Subscriptions
export async function getSubscriptions(params: {
  limit: number
  offset: number
  status?: string
}): Promise<SubscriptionsListResponse> {
  const res = await apiClient.get('/admin/subscriptions', { params })
  return res.data
}

export async function getUserSubscription(userId: number): Promise<SubscriptionDetail> {
  const res = await apiClient.get(`/admin/subscriptions/${userId}`)
  return res.data
}

export async function exemptUser(userId: number): Promise<void> {
  const res = await apiClient.post(`/admin/subscriptions/${userId}/exempt`)
  return res.data
}

export async function removeExempt(userId: number): Promise<void> {
  const res = await apiClient.delete(`/admin/subscriptions/${userId}/exempt`)
  return res.data
}

export async function resetTrial(userId: number): Promise<void> {
  const res = await apiClient.post(`/admin/subscriptions/${userId}/reset-trial`)
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
