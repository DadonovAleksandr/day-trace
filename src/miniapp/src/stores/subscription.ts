import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { getSubscription, createCheckout } from '../api/subscription'
import type { SubscriptionInfo } from '../types'

export const useSubscriptionStore = defineStore('subscription', () => {
  const info = ref<SubscriptionInfo | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const hasAccess = computed(() => {
    if (!info.value) return true
    return ['not_started', 'trial', 'active', 'grace_period', 'exempt'].includes(info.value.status)
  })

  const isGracePeriod = computed(() => info.value?.status === 'grace_period')
  const isExpired = computed(() => info.value?.status === 'expired')
  const isBlocked = computed(() => info.value?.status === 'expired')

  async function fetchSubscription() {
    loading.value = true
    error.value = null
    try {
      info.value = await getSubscription()
    } catch (e: any) {
      error.value = e.response?.data?.message || e.message || 'Failed to load subscription'
    } finally {
      loading.value = false
    }
  }

  async function checkout(plan: 'monthly' | 'annual') {
    return createCheckout(plan)
  }

  return { info, loading, error, hasAccess, isGracePeriod, isExpired, isBlocked, fetchSubscription, checkout }
})
