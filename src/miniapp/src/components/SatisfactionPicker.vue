<script setup lang="ts">
withDefaults(defineProps<{
  modelValue: number | null
  readonly?: boolean
  size?: 'sm' | 'md'
}>(), {
  modelValue: null,
  readonly: false,
  size: 'md'
})

const emit = defineEmits<{
  'update:modelValue': [value: number]
}>()

const moods = [
  { value: 1, emoji: '\u{1F61E}', label: 'Плохо' },
  { value: 2, emoji: '\u{1F615}', label: 'Так себе' },
  { value: 3, emoji: '\u{1F610}', label: 'Нормально' },
  { value: 4, emoji: '\u{1F642}', label: 'Хорошо' },
  { value: 5, emoji: '\u{1F60A}', label: 'Отлично' },
]
</script>

<template>
  <div :class="['satisfaction-picker', `satisfaction-picker--${size}`, { 'satisfaction-picker--readonly': readonly }]">
    <button
      v-for="mood in moods"
      :key="mood.value"
      type="button"
      :class="['satisfaction-picker__btn', { 'satisfaction-picker__btn--active': modelValue === mood.value }]"
      :disabled="readonly"
      @click="emit('update:modelValue', mood.value)"
      :aria-label="mood.label"
      :title="mood.label"
    >
      <span :class="['satisfaction-picker__emoji', { 'satisfaction-picker__emoji--dimmed': modelValue !== null && modelValue !== mood.value }]">
        {{ mood.emoji }}
      </span>
    </button>
  </div>
</template>

<style scoped>
.satisfaction-picker {
  display: inline-flex;
  gap: 4px;
}

.satisfaction-picker--md .satisfaction-picker__emoji {
  font-size: 28px;
}

.satisfaction-picker--sm .satisfaction-picker__emoji {
  font-size: 18px;
}

.satisfaction-picker__btn {
  background: none;
  border: 2px solid transparent;
  padding: 4px 6px;
  cursor: pointer;
  border-radius: 10px;
  transition: all 200ms ease;
  line-height: 1;
}

.satisfaction-picker__btn--active {
  border-color: var(--tg-button-color, #3390ec);
  background: rgba(51, 144, 236, 0.08);
}

.satisfaction-picker__btn:not(:disabled):hover {
  transform: scale(1.15);
}

.satisfaction-picker__btn:not(:disabled):active {
  transform: scale(0.95);
}

.satisfaction-picker__emoji--dimmed {
  opacity: 0.4;
  filter: grayscale(0.6);
}

.satisfaction-picker--readonly .satisfaction-picker__btn {
  cursor: default;
  padding: 2px 3px;
}

.satisfaction-picker--readonly .satisfaction-picker__emoji--dimmed {
  display: none;
}

.satisfaction-picker--sm {
  gap: 2px;
}
</style>
