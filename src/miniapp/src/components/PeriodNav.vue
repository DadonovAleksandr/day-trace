<script setup lang="ts">
import AppIcon from './AppIcon.vue'

defineProps<{
  label: string
  canGoForward?: boolean
}>()

const emit = defineEmits<{
  prev: []
  next: []
}>()
</script>

<template>
  <div class="period-nav">
    <button class="period-nav__btn" @click="emit('prev')" aria-label="Назад">
      <AppIcon name="chevron-left" :size="20" />
    </button>
    <span class="period-nav__label">{{ label }}</span>
    <button
      class="period-nav__btn"
      :disabled="!canGoForward"
      @click="emit('next')"
      aria-label="Вперёд"
    >
      <AppIcon name="chevron-right" :size="20" />
    </button>
  </div>
</template>

<style scoped>
.period-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
  padding: 8px 0;
}

.period-nav__btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.06));
  border-radius: 10px;
  cursor: pointer;
  color: var(--tg-text-color, #000);
  transition: all 200ms ease;
  line-height: 0;
}

.period-nav__btn:hover:not(:disabled) {
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  border-color: transparent;
}

.period-nav__btn:active:not(:disabled) {
  transform: scale(0.93);
}

.period-nav__btn:disabled {
  opacity: 0.25;
  cursor: default;
}

.period-nav__label {
  font-size: 15px;
  font-weight: 600;
  min-width: 140px;
  text-align: center;
}
</style>
