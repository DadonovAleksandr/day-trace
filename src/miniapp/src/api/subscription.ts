import apiClient from './client'
import type { SubscriptionInfo } from '../types'

export async function getSubscription(): Promise<SubscriptionInfo> {
  const { data } = await apiClient.get('/subscription')
  return data
}

export async function createCheckout(plan: 'monthly' | 'annual'): Promise<{ invoice_link: string }> {
  const { data } = await apiClient.post('/subscription/checkout', { plan })
  return data
}
