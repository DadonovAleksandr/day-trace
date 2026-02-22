<script setup lang="ts">
import AppIcon from './AppIcon.vue'

withDefaults(defineProps<{
  modelValue: number
  readonly?: boolean
  size?: 'sm' | 'md'
}>(), {
  readonly: false,
  size: 'md'
})

const emit = defineEmits<{
  'update:modelValue': [value: number]
}>()
</script>

<template>
  <div :class="['star-picker', `star-picker--${size}`, { 'star-picker--readonly': readonly }]">
    <button
      v-for="n in 5"
      :key="n"
      type="button"
      :class="['star-picker__btn', { 'star-picker__btn--active': n <= modelValue }]"
      :disabled="readonly"
      @click="emit('update:modelValue', n)"
      :aria-label="`${n} из 5`"
    >
      <AppIcon
        :name="n <= modelValue ? 'star' : 'star-outline'"
        :size="size === 'sm' ? 14 : 22"
      />
    </button>
  </div>
</template>

<style scoped>
.star-picker {
  display: inline-flex;
  gap: 2px;
}

.star-picker__btn {
  background: none;
  border: none;
  padding: 2px;
  cursor: pointer;
  color: var(--tg-hint-color, #999);
  transition: color 200ms ease, transform 200ms ease;
  line-height: 0;
}

.star-picker__btn--active {
  color: var(--dt-star-color, #f59e0b);
}

.star-picker__btn:not(:disabled):hover {
  transform: scale(1.2);
}

.star-picker__btn:not(:disabled):active {
  transform: scale(0.9);
}

.star-picker--readonly .star-picker__btn {
  cursor: default;
  padding: 0;
}

.star-picker--sm {
  gap: 0;
}
</style>
