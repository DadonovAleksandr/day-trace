<script setup lang="ts">
import AppIcon from './AppIcon.vue'

defineProps<{
  title: string
  status: string
  eventCount?: number
  generating?: boolean
  locked?: boolean
  lockReason?: string
}>()

const emit = defineEmits<{
  generate: []
}>()
</script>

<template>
  <div class="summary">
    <div class="summary__header">
      <h3 class="summary__title">{{ title }}</h3>
      <span v-if="status === 'generated'" class="summary__badge summary__badge--success">
        <AppIcon name="check" :size="12" />
        {{ eventCount ?? 0 }} событий
      </span>
      <span v-else-if="status === 'generating'" class="summary__badge summary__badge--pending">
        <AppIcon name="loader" :size="12" class="summary__spinner" />
        Формируется
      </span>
      <span v-else-if="status === 'failed'" class="summary__badge summary__badge--error">
        <AppIcon name="alert-circle" :size="12" />
        Ошибка
      </span>
      <span v-else class="summary__badge summary__badge--empty">
        Нет итога
      </span>
    </div>

    <button
      class="summary__btn"
      :disabled="generating || locked"
      @click="!locked && emit('generate')"
    >
      <AppIcon
        :name="locked ? 'lock' : generating ? 'loader' : 'refresh'"
        :size="16"
        :class="{ 'summary__spinner': generating }"
      />
      {{ locked ? lockReason : generating ? 'Формируем...' : 'Сформировать итог' }}
    </button>
  </div>
</template>

<style scoped>
.summary {
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border-radius: 14px;
  padding: 14px 16px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
}

.summary__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.summary__title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
}

.summary__badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  padding: 3px 8px;
  border-radius: 20px;
  font-weight: 500;
}

.summary__badge--success {
  background: var(--dt-success-bg, rgba(76,175,80,0.1));
  color: var(--dt-success-text, #43a047);
}

.summary__badge--pending {
  background: var(--dt-warning-bg, rgba(255,152,0,0.1));
  color: var(--dt-warning-text, #ef6c00);
}

.summary__badge--error {
  background: var(--dt-error-bg, rgba(239,83,80,0.1));
  color: var(--dt-error-text, #e53935);
}

.summary__badge--empty {
  color: var(--tg-hint-color, #999);
}

.summary__btn {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 10px 16px;
  background: var(--tg-button-color, #2481cc);
  color: var(--tg-button-text-color, #fff);
  border: none;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 200ms ease;
}

.summary__btn:hover:not(:disabled) {
  filter: brightness(1.08);
}

.summary__btn:active:not(:disabled) {
  transform: scale(0.98);
}

.summary__btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.summary__spinner {
  animation: dt-spin 1s linear infinite;
}

@keyframes dt-spin {
  to { transform: rotate(360deg); }
}
</style>
