<script setup lang="ts">
import AppIcon from './AppIcon.vue'

withDefaults(defineProps<{
  message: string
  type?: 'error' | 'success' | 'warning'
}>(), {
  type: 'error'
})

const emit = defineEmits<{
  dismiss: []
}>()
</script>

<template>
  <div :class="['banner', `banner--${type}`]" @click="emit('dismiss')">
    <AppIcon
      :name="type === 'success' ? 'check' : type === 'warning' ? 'warning' : 'alert-circle'"
      :size="16"
      class="banner__icon"
    />
    <span class="banner__text">{{ message }}</span>
    <button class="banner__close" aria-label="Закрыть">
      <AppIcon name="x" :size="14" />
    </button>
  </div>
</template>

<style scoped>
.banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-radius: 10px;
  font-size: 13px;
  cursor: pointer;
  animation: dt-slide-in 0.25s ease;
  border: 1px solid transparent;
}

.banner--error {
  background: var(--dt-error-bg, rgba(239,83,80,0.08));
  border-color: var(--dt-error-border, rgba(239,83,80,0.15));
  color: var(--dt-error-text, #e53935);
}

.banner--success {
  background: var(--dt-success-bg, rgba(76,175,80,0.08));
  border-color: var(--dt-success-border, rgba(76,175,80,0.15));
  color: var(--dt-success-text, #43a047);
}

.banner--warning {
  background: var(--dt-warning-bg, rgba(255,152,0,0.08));
  border-color: var(--dt-warning-border, rgba(255,152,0,0.15));
  color: var(--dt-warning-text, #ef6c00);
}

.banner__icon {
  flex-shrink: 0;
}

.banner__text {
  flex: 1;
  line-height: 1.4;
}

.banner__close {
  background: none;
  border: none;
  padding: 2px;
  cursor: pointer;
  color: inherit;
  opacity: 0.6;
  line-height: 0;
  flex-shrink: 0;
}

@keyframes dt-slide-in {
  from { opacity: 0; transform: translateY(-8px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>
