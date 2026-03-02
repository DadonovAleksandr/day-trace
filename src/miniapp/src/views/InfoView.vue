<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue'
import AppIcon from '../components/AppIcon.vue'
import SubscriptionPaywall from '../components/SubscriptionPaywall.vue'
import { useTelegram } from '../composables/useTelegram'
import { useSubscriptionStore } from '../stores/subscription'

const { showBackButton, hideBackButton } = useTelegram()
const subscriptionStore = useSubscriptionStore()

const showLocalPaywall = ref(false)

const subscriptionStatus = computed(() => subscriptionStore.info?.status ?? null)
const daysRemaining = computed(() => subscriptionStore.info?.days_remaining)
const subscriptionExpiresAt = computed(() => {
  const d = subscriptionStore.info?.subscription_expires_at
  if (!d) return ''
  return new Date(d).toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' })
})

const showSubscribeButton = computed(() =>
  subscriptionStatus.value && ['trial', 'grace_period', 'expired'].includes(subscriptionStatus.value)
)

function openPaywall() {
  showLocalPaywall.value = true
}

function onPaywallPaid() {
  showLocalPaywall.value = false
}

function onPaywallDismiss() {
  showLocalPaywall.value = false
}

type SectionId = 'about' | 'guide' | 'payment' | 'contact'

const openSection = ref<SectionId | null>(null)

function toggle(id: SectionId) {
  openSection.value = openSection.value === id ? null : id
}

// BackButton: show when a section is expanded, hide when collapsed
watch(openSection, (newSection) => {
  if (newSection !== null) {
    showBackButton(() => { openSection.value = null })
  } else {
    hideBackButton()
  }
})

onUnmounted(() => {
  hideBackButton()
})

const appVersion = import.meta.env.VITE_APP_VERSION || 'dev'

const sections: { id: SectionId; icon: string; title: string; subtitle: string }[] = [
  { id: 'about', icon: 'book-open', title: 'О проекте', subtitle: 'Что такое Событник' },
  { id: 'guide', icon: 'today', title: 'Инструкция', subtitle: 'Как пользоваться' },
  { id: 'payment', icon: 'credit-card', title: 'Подписка', subtitle: 'Тариф и оплата' },
  { id: 'contact', icon: 'send', title: 'Связаться с нами', subtitle: 'Баги, предложения, вопросы' },
]
</script>

<template>
  <div class="info-view">
    <!-- Header (скрываем при открытой секции) -->
    <Transition name="fade">
      <h2 v-if="!openSection" class="view-title">О проекте</h2>
    </Transition>

    <!-- Sections list -->
    <div :class="['info-sections', { 'info-sections--expanded': openSection }]">
      <div
        v-for="section in sections"
        :key="section.id"
        :class="[
          'info-section',
          {
            'info-section--open': openSection === section.id,
            'info-section--hidden': openSection && openSection !== section.id,
          },
        ]"
      >
        <!-- Section header (кнопка) -->
        <button class="section-header" @click="toggle(section.id)">
          <span class="section-header__left">
            <AppIcon :name="section.icon" :size="22" />
            <span class="section-header__text">
              <span class="section-header__title">{{ section.title }}</span>
              <span class="section-header__subtitle">{{ section.subtitle }}</span>
            </span>
          </span>
          <AppIcon
            :name="openSection === section.id ? 'chevron-up' : 'chevron-down'"
            :size="18"
            class="section-header__arrow"
          />
        </button>

        <!-- Section content -->
        <Transition name="section-expand">
          <div v-if="openSection === section.id" class="section-body">
            <!-- О проекте -->
            <template v-if="section.id === 'about'">
              <div class="section-content">
                <p class="section-text section-text--lead">
                  Событник — это ваш личный дневник событий и рефлексии.
                </p>
                <p class="section-text">
                  Каждый день наполнен событиями, но без записи они быстро стираются
                  из памяти. Событник помогает фиксировать главное событие дня и видеть,
                  как складывается ваша жизнь — день за днём, неделя за неделей, месяц за месяцем.
                </p>
                <div class="section-highlight">
                  <AppIcon name="sparkles" :size="18" />
                  <span>Иерархия итогов: день → неделя → месяц → год</span>
                </div>
                <p class="section-text">
                  Проект создан для тех, кто хочет осмысленно проживать каждый день:
                  замечать важное, анализировать паттерны и видеть общую картину своей жизни.
                </p>
                <p class="section-text">
                  Не нужно писать длинные тексты — достаточно короткой заметки
                  о ключевом событии дня. При регулярных записях и выборе главных
                  событий периода постепенно складывается осмысленная история года.
                </p>
                <p class="section-text section-text--hint">
                  Сейчас действует ограничение: одна запись на день.
                  Дату новой записи можно выбрать только в пределах последних 30 дней.
                </p>
              </div>
            </template>

            <!-- Инструкция -->
            <template v-if="section.id === 'guide'">
              <div class="section-content">
                <div class="guide-step">
                  <span class="guide-step__num">1</span>
                  <div class="guide-step__body">
                    <h4 class="guide-step__title">Записывайте события дня</h4>
                    <p class="section-text">
                      В конце дня (или в течение него) добавляйте короткую запись
                      о главном событии дня. Сейчас в приложении доступна одна запись
                      на дату, а дату новой записи можно выбрать в пределах последних 30 дней.
                    </p>
                  </div>
                </div>

                <div class="guide-step">
                  <span class="guide-step__num">2</span>
                  <div class="guide-step__body">
                    <h4 class="guide-step__title">Оценивайте важность</h4>
                    <p class="section-text">
                      Для каждого события можно указать важность от 1 до 5 звёзд.
                      Это поможет выделить ключевые моменты при формировании итогов.
                    </p>
                    <p class="section-text section-text--hint">
                      Можно отключить в настройках, если вам это не нужно.
                    </p>
                  </div>
                </div>

                <div class="guide-step">
                  <span class="guide-step__num">3</span>
                  <div class="guide-step__body">
                    <h4 class="guide-step__title">Оцените удовлетворённость днём</h4>
                    <p class="section-text">
                      В конце дня оцените, насколько вы довольны прожитым днём. Это помогает
                      отслеживать общее настроение и выявлять тенденции.
                    </p>
                    <p class="section-text section-text--hint">
                      Также отключается в настройках.
                    </p>
                  </div>
                </div>

                <div class="guide-step">
                  <span class="guide-step__num">4</span>
                  <div class="guide-step__body">
                    <h4 class="guide-step__title">Выбирайте главное</h4>
                    <p class="section-text">
                      Когда период завершится, откройте раздел недели, месяца или года
                      и вручную выберите самое важное событие. Напоминание в Telegram
                      может подсказать, что пора вернуться к итогу.
                    </p>
                    <p class="section-text section-text--hint">
                      Важно: итог месяца выбирается из недельных итогов,
                      а итог года — из месячных.
                    </p>
                    <p class="section-text section-text--hint">
                      После формирования итога недели события этой недели
                      нельзя редактировать или удалять.
                    </p>
                  </div>
                </div>
              </div>
            </template>

            <!-- Подписка -->
            <template v-if="section.id === 'payment'">
              <div class="section-content">
                <!-- not_started -->
                <template v-if="!subscriptionStatus || subscriptionStatus === 'not_started'">
                  <div class="payment-badge">
                    <span class="payment-badge__text">Пробный период</span>
                  </div>
                  <p class="section-text">
                    Пробный период начнётся с момента первой записи (30 дней бесплатно).
                  </p>
                </template>

                <!-- trial -->
                <template v-else-if="subscriptionStatus === 'trial'">
                  <div class="payment-badge">
                    <span class="payment-badge__text">Пробный период</span>
                  </div>
                  <p class="section-text">
                    Пробный период: осталось {{ daysRemaining }} дней.
                  </p>
                </template>

                <!-- active -->
                <template v-else-if="subscriptionStatus === 'active'">
                  <div class="payment-badge payment-badge--active">
                    <span class="payment-badge__text">Подписка активна</span>
                  </div>
                  <p class="section-text">
                    Подписка активна до {{ subscriptionExpiresAt }}.
                  </p>
                </template>

                <!-- grace_period -->
                <template v-else-if="subscriptionStatus === 'grace_period'">
                  <div class="payment-badge payment-badge--warning">
                    <span class="payment-badge__text">Льготный период</span>
                  </div>
                  <p class="section-text">
                    Подписка истекла. Осталось {{ daysRemaining }} дней льготного периода.
                  </p>
                </template>

                <!-- exempt -->
                <template v-else-if="subscriptionStatus === 'exempt'">
                  <div class="payment-badge payment-badge--active">
                    <span class="payment-badge__text">Специальный доступ</span>
                  </div>
                  <p class="section-text">
                    Специальный доступ (бесплатно навсегда).
                  </p>
                </template>

                <!-- expired -->
                <template v-else-if="subscriptionStatus === 'expired'">
                  <div class="payment-badge payment-badge--expired">
                    <span class="payment-badge__text">Подписка истекла</span>
                  </div>
                  <p class="section-text">
                    Подписка истекла — требуется оплата.
                  </p>
                </template>

                <!-- Subscribe button -->
                <button
                  v-if="showSubscribeButton"
                  class="subscribe-btn"
                  @click="openPaywall"
                >
                  Оформить подписку
                </button>

                <div class="section-highlight section-highlight--warm">
                  <AppIcon name="heart" :size="18" />
                  <span>
                    Если хотите поддержать проект или есть вопросы по оплате,
                    напишите нам. Мы всегда готовы помочь.
                  </span>
                </div>
                <a
                  href="https://t.me/a_snafu"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="action-card action-card--warm"
                >
                  <span class="action-card__icon action-card__icon--warm">
                    <AppIcon name="heart" :size="20" />
                  </span>
                  <span class="action-card__body">
                    <span class="action-card__value">Поддержать проект</span>
                    <span class="action-card__label">Донаты и помощь</span>
                  </span>
                  <AppIcon name="chevron-right" :size="18" class="action-card__arrow" />
                </a>
              </div>
            </template>

            <!-- Связаться с нами -->
            <template v-if="section.id === 'contact'">
              <div class="section-content">
                <p class="section-text">
                  Если у вас есть вопросы, предложения или вы столкнулись
                  с проблемой — мы всегда на связи.
                </p>
                <a
                  href="https://t.me/a_snafu"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="action-card action-card--telegram"
                >
                  <span class="action-card__icon action-card__icon--telegram">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                      <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.64 6.8c-.15 1.58-.8 5.42-1.13 7.19-.14.75-.42 1-.68 1.03-.58.05-1.02-.38-1.58-.75-.88-.58-1.38-.94-2.23-1.5-.99-.65-.35-1.01.22-1.59.15-.15 2.71-2.48 2.76-2.69a.2.2 0 00-.05-.18c-.06-.05-.14-.03-.21-.02-.09.02-1.49.95-4.22 2.79-.4.27-.76.41-1.08.4-.36-.01-1.04-.2-1.55-.37-.63-.2-1.12-.31-1.08-.66.02-.18.27-.36.74-.55 2.92-1.27 4.86-2.11 5.83-2.51 2.78-1.16 3.35-1.36 3.73-1.36.08 0 .27.02.39.12.1.08.13.19.14.27-.01.06.01.24 0 .38z" fill="currentColor"/>
                    </svg>
                  </span>
                  <span class="action-card__body">
                    <span class="action-card__label">Telegram</span>
                    <span class="action-card__value">@a_snafu</span>
                  </span>
                  <AppIcon name="chevron-right" :size="18" class="action-card__arrow" />
                </a>
                <p class="section-text section-text--hint">
                  Пишите по любым вопросам: баги, предложения,
                  вопросы по оплате или просто обратная связь.
                </p>
              </div>
            </template>
          </div>
        </Transition>
      </div>
    </div>

    <p v-if="!openSection" class="info-version">Версия {{ appVersion }}</p>

    <!-- Local paywall overlay -->
    <SubscriptionPaywall
      v-if="showLocalPaywall"
      :show-later="true"
      @paid="onPaywallPaid"
      @dismiss="onPaywallDismiss"
    />
  </div>
</template>

<style scoped>
.info-view {
  max-width: 600px;
  margin: 0 auto;
}

/* Header */
.info-header {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 8px 0 20px;
}

.info-logo {
  width: 56px;
  height: 56px;
  border-radius: 16px;
  background: linear-gradient(135deg, var(--tg-button-color, #2481cc), var(--tg-link-color, #3390ec));
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 12px;
}

.info-logo__icon {
  font-size: 22px;
  font-weight: 800;
  color: var(--tg-button-text-color, #fff);
  letter-spacing: -0.5px;
}

.view-title {
  margin: 0 0 12px;
  font-size: 22px;
  font-weight: 700;
  text-align: center;
}

.info-subtitle {
  margin: 4px 0 0;
  font-size: 14px;
  color: var(--tg-hint-color, #999);
}

/* Sections */
.info-sections {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.info-section {
  border-radius: 14px;
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.04));
  overflow: hidden;
  transition: all 300ms ease;
}

.info-section--open {
  flex: 1;
  min-height: 0;
}

.info-section--hidden {
  opacity: 0;
  max-height: 0;
  margin: 0;
  padding: 0;
  border: none;
  pointer-events: none;
}

/* Section header */
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 14px 16px;
  background: none;
  border: none;
  color: var(--tg-text-color, #000);
  font-size: 16px;
  font-weight: 700;
  font-family: inherit;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: background 200ms ease;
}

.section-header:active {
  background: rgba(0, 0, 0, 0.03);
}

.dark .section-header:active {
  background: rgba(255, 255, 255, 0.05);
}

.section-header__left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.section-header__text {
  display: flex;
  flex-direction: column;
  gap: 3px;
  text-align: left;
}

.section-header__title {
  line-height: 1.2;
}

.section-header__subtitle {
  font-size: 12px;
  font-weight: 400;
  color: var(--tg-hint-color, #999);
  line-height: 1.3;
}

.section-header__arrow {
  color: var(--tg-hint-color, #999);
  transition: transform 200ms ease;
}

/* Section body */
.section-body {
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.section-content {
  padding: 0 16px 16px;
}

/* Text styles */
.section-text {
  font-size: 14px;
  line-height: 1.6;
  color: var(--tg-text-color, #000);
  margin: 0 0 12px;
}

.section-text:last-child {
  margin-bottom: 0;
}

.section-text--lead {
  font-size: 15px;
  font-weight: 600;
  color: var(--tg-text-color, #000);
}

.section-text--hint {
  font-size: 13px;
  color: var(--tg-hint-color, #999);
  font-style: italic;
}

/* Highlight block */
.section-highlight {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 14px;
  border-radius: 12px;
  background: color-mix(in srgb, var(--tg-button-color, #2481cc) 10%, transparent);
  color: var(--tg-text-color, #000);
  font-size: 14px;
  line-height: 1.5;
  margin: 12px 0;
}

.section-highlight--warm {
  background: color-mix(in srgb, var(--dt-star-color, #f59e0b) 12%, transparent);
}

/* Guide steps */
.guide-step {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
}

.guide-step:last-child {
  margin-bottom: 0;
}

.guide-step__num {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  font-size: 14px;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-top: 2px;
}

.guide-step__body {
  flex: 1;
  min-width: 0;
}

.guide-step__title {
  margin: 0 0 4px;
  font-size: 14px;
  font-weight: 600;
}

.guide-step__body .section-text {
  margin-bottom: 4px;
}

/* Payment badge */
.payment-badge {
  display: inline-flex;
  align-items: center;
  padding: 6px 14px;
  border-radius: 20px;
  background: linear-gradient(135deg, var(--tg-button-color, #2481cc), var(--tg-link-color, #3390ec));
  color: var(--tg-button-text-color, #fff);
  font-size: 14px;
  font-weight: 600;
  margin-bottom: 12px;
}

.payment-badge--active {
  background: linear-gradient(135deg, #34c759, #30d158);
}

.payment-badge--warning {
  background: linear-gradient(135deg, #f59e0b, #f5a623);
}

.payment-badge--expired {
  background: linear-gradient(135deg, #e53935, #ef5350);
}

/* Subscribe button */
.subscribe-btn {
  width: 100%;
  padding: 14px;
  margin: 16px 0;
  border: none;
  border-radius: 12px;
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  font-size: 15px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: all 200ms ease;
  -webkit-tap-highlight-color: transparent;
}

.subscribe-btn:active {
  transform: scale(0.97);
}

/* Action card */
.action-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  border-radius: 12px;
  background: var(--tg-bg-color, #fff);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.06));
  text-decoration: none;
  color: var(--tg-text-color, #000);
  transition: all 200ms ease;
  margin: 16px 0;
}

.action-card:active {
  transform: scale(0.98);
}

.action-card__icon {
  flex-shrink: 0;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
}

.action-card__icon--telegram {
  background: #26a5e4;
  color: #fff;
}

.action-card__icon--warm {
  background: color-mix(in srgb, var(--dt-star-color, #f59e0b) 15%, transparent);
  color: var(--dt-star-color, #f59e0b);
}

.action-card__body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.action-card__label {
  font-size: 13px;
  color: var(--tg-hint-color, #999);
}

.action-card__value {
  font-size: 15px;
  font-weight: 600;
}

.action-card__arrow {
  color: var(--tg-hint-color, #999);
}

/* Transitions */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 250ms ease, transform 250ms ease;
}

.fade-enter-from {
  opacity: 0;
  transform: translateY(-8px);
}

.fade-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}

.section-expand-enter-active {
  transition: all 300ms ease;
}

.section-expand-leave-active {
  transition: all 200ms ease;
}

.section-expand-enter-from {
  opacity: 0;
  max-height: 0;
}

.section-expand-enter-to {
  opacity: 1;
  max-height: 800px;
}

.section-expand-leave-from {
  opacity: 1;
  max-height: 800px;
}

.section-expand-leave-to {
  opacity: 0;
  max-height: 0;
}

.info-version {
  text-align: center;
  font-size: 12px;
  color: var(--tg-hint-color, #999);
  margin: 24px 0 8px;
}
</style>
