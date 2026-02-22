<script setup lang="ts">
import AppIcon from './AppIcon.vue'

withDefaults(defineProps<{
  message: string
  icon?: string
}>(), {
  icon: 'note-empty'
})
</script>

<template>
  <div class="empty-state">
    <div class="empty-state__icon">
      <AppIcon :name="icon" :size="48" />
    </div>
    <p class="empty-state__text">{{ message }}</p>
    <slot />
  </div>
</template>

<style scoped>
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 40px 20px;
  animation: dt-fade-up 0.3s ease;
}

.empty-state__icon {
  color: var(--tg-hint-color, #999);
  opacity: 0.35;
  margin-bottom: 12px;
}

.empty-state__text {
  color: var(--tg-hint-color, #999);
  font-size: 14px;
  text-align: center;
  margin: 0;
  line-height: 1.5;
}

@keyframes dt-fade-up {
  from { opacity: 0; transform: translateY(12px); }
  to { opacity: 1; transform: translateY(0); }
}
</style>
