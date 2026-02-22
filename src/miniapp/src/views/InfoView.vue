<script setup lang="ts">
import { ref } from 'vue'
import AppIcon from '../components/AppIcon.vue'

type SectionId = 'about' | 'guide' | 'payment' | 'contact'

const openSection = ref<SectionId | null>(null)

function toggle(id: SectionId) {
  openSection.value = openSection.value === id ? null : id
}

const sections: { id: SectionId; icon: string; title: string }[] = [
  { id: 'about', icon: 'book-open', title: 'О проекте' },
  { id: 'guide', icon: 'today', title: 'Инструкция' },
  { id: 'payment', icon: 'credit-card', title: 'Оплата' },
  { id: 'contact', icon: 'send', title: 'Связаться с нами' },
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
            <AppIcon :name="section.icon" :size="20" />
            <span class="section-header__title">{{ section.title }}</span>
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
                  Каждый день наполнен событиями, но без записи они быстро забываются.
                  Событник помогает фиксировать важные моменты и отслеживать, как складывается
                  ваша жизнь — день за днём, неделя за неделей, месяц за месяцем.
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
                  Не нужно писать длинные тексты — достаточно коротких заметок
                  о ключевых событиях дня. Со временем из этих маленьких записей
                  формируется полная история вашего года.
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
                      В конце дня (или в течение него) добавляйте короткие записи о произошедших
                      событиях. Что случилось? Что вас порадовало или огорчило?
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
                      В конце недели, месяца и года Событник предложит вам
                      выбрать самое важное событие периода. Так из ежедневных
                      записей складывается осмысленная история вашего года.
                    </p>
                  </div>
                </div>
              </div>
            </template>

            <!-- Оплата -->
            <template v-if="section.id === 'payment'">
              <div class="section-content">
                <div class="payment-badge">
                  <span class="payment-badge__text">Бесплатно 3 месяца</span>
                </div>
                <p class="section-text">
                  Первые 3 месяца использования — полностью бесплатны.
                  Этого достаточно, чтобы попробовать все функции и понять,
                  подходит ли вам Событник.
                </p>
                <p class="section-text">
                  Мы бы с радостью сделали сервис полностью бесплатным,
                  но помимо работы разработчика есть затраты на аренду серверов,
                  электроэнергию, обслуживание инфраструктуры и другие расходы.
                </p>
                <p class="section-text">
                  Мы стремимся установить минимальную стоимость подписки,
                  распределяя финансовую нагрузку между всеми пользователями.
                  Наша цель — не заработок, а устойчивая работа сервиса.
                </p>
                <div class="section-highlight section-highlight--warm">
                  <AppIcon name="heart" :size="18" />
                  <span>
                    Если у вас нет возможности оплатить подписку — напишите нам.
                    Мы всегда готовы пойти навстречу.
                  </span>
                </div>
                <p class="section-text">
                  Также мы с благодарностью принимаем донаты от тех,
                  кто хочет поддержать развитие проекта.
                </p>
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
                  class="contact-card"
                >
                  <span class="contact-card__icon">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
                      <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.64 6.8c-.15 1.58-.8 5.42-1.13 7.19-.14.75-.42 1-.68 1.03-.58.05-1.02-.38-1.58-.75-.88-.58-1.38-.94-2.23-1.5-.99-.65-.35-1.01.22-1.59.15-.15 2.71-2.48 2.76-2.69a.2.2 0 00-.05-.18c-.06-.05-.14-.03-.21-.02-.09.02-1.49.95-4.22 2.79-.4.27-.76.41-1.08.4-.36-.01-1.04-.2-1.55-.37-.63-.2-1.12-.31-1.08-.66.02-.18.27-.36.74-.55 2.92-1.27 4.86-2.11 5.83-2.51 2.78-1.16 3.35-1.36 3.73-1.36.08 0 .27.02.39.12.1.08.13.19.14.27-.01.06.01.24 0 .38z" fill="currentColor"/>
                    </svg>
                  </span>
                  <span class="contact-card__body">
                    <span class="contact-card__label">Telegram</span>
                    <span class="contact-card__value">@a_snafu</span>
                  </span>
                  <AppIcon name="chevron-right" :size="18" class="contact-card__arrow" />
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
  margin: 0;
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
  gap: 8px;
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
  font-size: 15px;
  font-weight: 600;
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

.section-header__title {
  line-height: 1.2;
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

/* Contact card */
.contact-card {
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

.contact-card:active {
  transform: scale(0.98);
}

.contact-card__icon {
  flex-shrink: 0;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: #26a5e4;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}

.contact-card__body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.contact-card__label {
  font-size: 13px;
  color: var(--tg-hint-color, #999);
}

.contact-card__value {
  font-size: 15px;
  font-weight: 600;
}

.contact-card__arrow {
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
</style>
