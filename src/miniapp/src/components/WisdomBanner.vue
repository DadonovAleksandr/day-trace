<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getRandomWisdom } from '../api/wisdoms'
import type { WisdomResponse } from '../types'
import AppIcon from './AppIcon.vue'

const emit = defineEmits<{ hidden: [] }>()

const wisdom = ref<WisdomResponse | null>(null)
const visible = ref(false)
const hiding = ref(false)

onMounted(async () => {
  try {
    wisdom.value = await getRandomWisdom()
    // Small delay for mount animation
    requestAnimationFrame(() => {
      visible.value = true
    })
    // Auto-hide after ~4 seconds
    setTimeout(() => {
      hiding.value = true
      setTimeout(() => {
        visible.value = false
        emit('hidden')
      }, 600) // match CSS transition duration
    }, 4000)
  } catch {
    // Silently fail — wisdom is decorative, not critical
    emit('hidden')
  }
})
</script>

<template>
  <div
    v-if="wisdom && visible"
    :class="['wisdom-banner', { 'wisdom-banner--hiding': hiding }]"
  >
    <div class="wisdom-banner__icon">
      <AppIcon name="sparkles" :size="16" />
    </div>
    <p class="wisdom-banner__text">{{ wisdom.text }}</p>
    <p v-if="wisdom.author" class="wisdom-banner__author">
      &mdash; {{ wisdom.author }}
    </p>
  </div>
</template>

<style scoped>
.wisdom-banner {
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.04));
  border-radius: 14px;
  padding: 16px 20px;
  margin-bottom: 12px;
  text-align: center;
  max-width: 480px;
  margin-left: auto;
  margin-right: auto;

  /* Fade-in animation */
  animation: wisdom-fade-in 0.5s ease forwards;
  opacity: 0;
  transform: translateY(-8px);

  /* Transition for fade-out */
  transition: opacity 0.6s ease, transform 0.6s ease;
}

.wisdom-banner--hiding {
  opacity: 0 !important;
  transform: translateY(-8px) !important;
}

@keyframes wisdom-fade-in {
  from {
    opacity: 0;
    transform: translateY(-8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.wisdom-banner__icon {
  display: flex;
  justify-content: center;
  margin-bottom: 8px;
  color: var(--tg-link-color, #2481cc);
  opacity: 0.6;
}

.wisdom-banner__text {
  font-family: 'Caveat', cursive;
  font-size: 21px;
  font-weight: 600;
  line-height: 1.3;
  color: var(--tg-text-color, #000);
  margin: 0;
}

.wisdom-banner__author {
  font-size: 13px;
  color: var(--tg-hint-color, #999);
  margin: 8px 0 0;
  font-style: normal;
}
</style>
