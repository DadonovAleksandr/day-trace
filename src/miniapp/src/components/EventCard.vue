<script setup lang="ts">
import type { EventItem } from '../types'
import StarPicker from './StarPicker.vue'
import AppIcon from './AppIcon.vue'

defineProps<{
  event: EventItem
  editable?: boolean
}>()

const emit = defineEmits<{
  edit: [event: EventItem]
  delete: [event: EventItem]
}>()
</script>

<template>
  <div class="event-card">
    <div class="event-card__body">
      <p class="event-card__text">{{ event.text }}</p>
      <div class="event-card__meta">
        <StarPicker :model-value="event.importance" readonly size="sm" />
        <span class="event-card__date">{{ event.local_date }}</span>
      </div>
    </div>
    <div v-if="editable" class="event-card__actions">
      <button class="event-card__action" @click="emit('edit', event)" aria-label="Редактировать">
        <AppIcon name="edit" :size="16" />
      </button>
      <button class="event-card__action event-card__action--danger" @click="emit('delete', event)" aria-label="Удалить">
        <AppIcon name="trash" :size="16" />
      </button>
    </div>
  </div>
</template>

<style scoped>
.event-card {
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border-radius: 12px;
  padding: 12px 14px;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
  transition: box-shadow 200ms ease;
}

.event-card__body {
  flex: 1;
  min-width: 0;
}

.event-card__text {
  margin: 0;
  word-break: break-word;
  font-size: 14px;
  line-height: 1.45;
}

.event-card__meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 6px;
  gap: 8px;
}

.event-card__date {
  font-size: 11px;
  color: var(--tg-hint-color, #999);
  white-space: nowrap;
}

.event-card__actions {
  display: flex;
  gap: 2px;
  flex-shrink: 0;
}

.event-card__action {
  background: none;
  border: none;
  padding: 6px;
  cursor: pointer;
  color: var(--tg-hint-color, #999);
  border-radius: 8px;
  transition: all 200ms ease;
  line-height: 0;
}

.event-card__action:hover {
  background: rgba(128, 128, 128, 0.08);
  color: var(--tg-text-color, #000);
}

.event-card__action--danger:hover {
  color: var(--dt-error-text, #e53935);
  background: var(--dt-error-bg, rgba(239,83,80,0.08));
}
</style>
