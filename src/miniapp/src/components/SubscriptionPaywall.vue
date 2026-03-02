<script setup lang="ts">
import { ref } from 'vue'
import { useSubscriptionStore } from '../stores/subscription'
import { useHaptic } from '../composables/useHaptic'
import AppIcon from './AppIcon.vue'

const props = defineProps<{ showLater: boolean }>()
const emit = defineEmits<{ paid: []; dismiss: [] }>()

const subscriptionStore = useSubscriptionStore()
const haptic = useHaptic()

const selectedPlan = ref<'monthly' | 'annual'>('monthly')
const paying = ref(false)

const plans = {
  monthly: { price: 100, label: '100', period: 'месяц' },
  annual: { price: 960, label: '960', period: 'год' },
}

function selectPlan(plan: 'monthly' | 'annual') {
  haptic.selectionChanged()
  selectedPlan.value = plan
}

async function handlePayment() {
  if (paying.value) return
  haptic.impact('medium')
  paying.value = true

  try {
    const { invoice_link } = await subscriptionStore.checkout(selectedPlan.value)
    const tg = window.Telegram?.WebApp

    if (tg?.openInvoice) {
      tg.openInvoice(invoice_link, (status: string) => {
        paying.value = false
        if (status === 'paid') {
          haptic.notification('success')
          subscriptionStore.fetchSubscription()
          emit('paid')
        }
      })
    } else {
      // Fallback for environments without openInvoice
      window.open(invoice_link, '_blank')
      paying.value = false
    }
  } catch {
    paying.value = false
  }
}

function handleLater() {
  haptic.impact('light')
  emit('dismiss')
}
</script>

<template>
  <div class="paywall">
    <div class="paywall__container">
      <!-- Logo -->
      <div class="paywall__logo">
        <AppIcon name="sparkles" :size="28" />
      </div>

      <!-- Title -->
      <h1 class="paywall__title">DayTrace Premium</h1>
      <p class="paywall__subtitle">Ваш личный журнал рефлексии</p>

      <!-- Features -->
      <ul class="paywall__features">
        <li class="paywall__feature">
          <AppIcon name="check" :size="18" class="paywall__feature-icon" />
          <span>Все записи без ограничений</span>
        </li>
        <li class="paywall__feature">
          <AppIcon name="check" :size="18" class="paywall__feature-icon" />
          <span>Итоги дня, недели, месяца, года</span>
        </li>
        <li class="paywall__feature">
          <AppIcon name="check" :size="18" class="paywall__feature-icon" />
          <span>История и аналитика</span>
        </li>
        <li class="paywall__feature">
          <AppIcon name="check" :size="18" class="paywall__feature-icon" />
          <span>Telegram интеграция</span>
        </li>
      </ul>

      <!-- Plan selector -->
      <div class="paywall__plans">
        <button
          :class="['paywall__plan', { 'paywall__plan--selected': selectedPlan === 'monthly' }]"
          @click="selectPlan('monthly')"
        >
          <span class="paywall__plan-price">{{ plans.monthly.label }}</span>
          <AppIcon name="star" :size="14" class="paywall__plan-star" />
          <span class="paywall__plan-period">/ {{ plans.monthly.period }}</span>
        </button>
        <button
          :class="['paywall__plan', { 'paywall__plan--selected': selectedPlan === 'annual' }]"
          @click="selectPlan('annual')"
        >
          <span class="paywall__plan-price">{{ plans.annual.label }}</span>
          <AppIcon name="star" :size="14" class="paywall__plan-star" />
          <span class="paywall__plan-period">/ {{ plans.annual.period }}</span>
          <span class="paywall__plan-badge">-20%</span>
        </button>
      </div>

      <!-- Pay button -->
      <button
        class="paywall__pay-btn"
        :disabled="paying"
        @click="handlePayment"
      >
        <span v-if="paying" class="paywall__pay-spinner" />
        <template v-else>
          Оплатить {{ plans[selectedPlan].label }}
          <AppIcon name="star" :size="16" class="paywall__pay-star" />
        </template>
      </button>

      <!-- Later button (grace period only) -->
      <button
        v-if="props.showLater"
        class="paywall__later-btn"
        @click="handleLater"
      >
        Позже
      </button>
    </div>
  </div>
</template>

<style scoped>
.paywall {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 200;
  background: var(--tg-bg-color, #ffffff);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  overflow-y: auto;
}

.paywall__container {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  max-width: 360px;
  width: 100%;
}

/* Logo */
.paywall__logo {
  width: 64px;
  height: 64px;
  border-radius: 20px;
  background: linear-gradient(135deg, var(--tg-button-color, #2481cc), var(--tg-link-color, #3390ec));
  color: var(--tg-button-text-color, #fff);
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 20px;
}

/* Title */
.paywall__title {
  margin: 0;
  font-size: 24px;
  font-weight: 700;
  color: var(--tg-text-color, #000);
}

.paywall__subtitle {
  margin: 6px 0 0;
  font-size: 14px;
  color: var(--tg-hint-color, #999);
}

/* Features */
.paywall__features {
  list-style: none;
  margin: 28px 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
  text-align: left;
}

.paywall__feature {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 15px;
  color: var(--tg-text-color, #000);
}

.paywall__feature-icon {
  flex-shrink: 0;
  color: var(--tg-button-color, #2481cc);
}

/* Plan selector */
.paywall__plans {
  display: flex;
  gap: 10px;
  width: 100%;
  margin-top: 28px;
}

.paywall__plan {
  flex: 1;
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-wrap: wrap;
  gap: 3px;
  padding: 14px 10px;
  border-radius: 14px;
  border: 2px solid var(--dt-card-border, rgba(0, 0, 0, 0.08));
  background: var(--tg-secondary-bg-color, #f5f5f5);
  color: var(--tg-text-color, #000);
  font-size: 15px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: all 200ms ease;
  -webkit-tap-highlight-color: transparent;
}

.paywall__plan--selected {
  border-color: var(--tg-button-color, #2481cc);
  background: color-mix(in srgb, var(--tg-button-color, #2481cc) 8%, transparent);
}

.paywall__plan-price {
  font-size: 18px;
  font-weight: 700;
}

.paywall__plan-star {
  color: var(--dt-star-color, #f59e0b);
}

.paywall__plan-period {
  font-weight: 400;
  color: var(--tg-hint-color, #999);
  font-size: 13px;
}

.paywall__plan-badge {
  position: absolute;
  top: -8px;
  right: -4px;
  padding: 2px 8px;
  border-radius: 10px;
  background: linear-gradient(135deg, var(--tg-button-color, #2481cc), var(--tg-link-color, #3390ec));
  color: var(--tg-button-text-color, #fff);
  font-size: 11px;
  font-weight: 700;
}

/* Pay button */
.paywall__pay-btn {
  width: 100%;
  margin-top: 24px;
  padding: 16px;
  border: none;
  border-radius: 14px;
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  font-size: 16px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  transition: all 200ms ease;
  -webkit-tap-highlight-color: transparent;
}

.paywall__pay-btn:active:not(:disabled) {
  transform: scale(0.97);
}

.paywall__pay-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.paywall__pay-star {
  color: var(--tg-button-text-color, #fff);
}

.paywall__pay-spinner {
  width: 20px;
  height: 20px;
  border: 2.5px solid rgba(255, 255, 255, 0.3);
  border-top-color: var(--tg-button-text-color, #fff);
  border-radius: 50%;
  animation: paywall-spin 0.7s linear infinite;
}

@keyframes paywall-spin {
  to { transform: rotate(360deg); }
}

/* Later button */
.paywall__later-btn {
  margin-top: 14px;
  padding: 10px 24px;
  border: none;
  background: none;
  color: var(--tg-hint-color, #999);
  font-size: 14px;
  font-family: inherit;
  cursor: pointer;
  transition: color 200ms ease;
  -webkit-tap-highlight-color: transparent;
}

.paywall__later-btn:active {
  color: var(--tg-text-color, #000);
}
</style>
