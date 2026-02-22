<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { getRandomWisdom } from '../api/wisdoms'
import type { WisdomResponse } from '../types'
import { useSettingsStore } from '../stores/settings'
import AppIcon from './AppIcon.vue'

const DEFAULT_DURATION = 10

const emit = defineEmits<{ hidden: [] }>()
const settingsStore = useSettingsStore()

const wisdom = ref<WisdomResponse | null>(null)
const hiding = ref(false)
const loadingWisdom = ref(true)
let hideTimer: ReturnType<typeof setTimeout> | null = null

function getDuration(): number {
  const val = settingsStore.settings?.wisdom_duration
  if (val && val >= 3 && val <= 60) return val
  return DEFAULT_DURATION
}

function dismiss() {
  if (hiding.value) return
  if (hideTimer) clearTimeout(hideTimer)
  hiding.value = true
  setTimeout(() => {
    emit('hidden')
  }, 500)
}

onMounted(async () => {
  try {
    wisdom.value = await getRandomWisdom()
    loadingWisdom.value = false
    const duration = getDuration() * 1000
    hideTimer = setTimeout(dismiss, duration)
  } catch {
    emit('hidden')
  }
})

onUnmounted(() => {
  if (hideTimer) clearTimeout(hideTimer)
})
</script>

<template>
  <div
    :class="['wisdom-screen', { 'wisdom-screen--hiding': hiding }]"
    @click="dismiss"
  >
    <Transition name="wisdom-content">
      <div v-if="wisdom && !loadingWisdom" class="wisdom-screen__body">
        <div class="wisdom-screen__icon">
          <AppIcon name="sparkles" :size="22" />
        </div>
        <p class="wisdom-screen__text">{{ wisdom.text }}</p>
        <p v-if="wisdom.author" class="wisdom-screen__author">
          &mdash; {{ wisdom.author }}
        </p>
      </div>
    </Transition>
    <p v-if="wisdom && !loadingWisdom" class="wisdom-screen__hint">tap to dismiss</p>
  </div>
</template>

<style scoped>
.wisdom-screen {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 56px; /* above bottom tabs */
  z-index: 50;
  background: var(--tg-bg-color, #ffffff);

  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 32px 28px;

  transition: opacity 0.5s ease;
  cursor: pointer;
}

.wisdom-screen--hiding {
  opacity: 0;
}

.wisdom-screen__body {
  text-align: center;
  max-width: 420px;
}

/* Transition for wisdom content appearing */
.wisdom-content-enter-active {
  transition: all 0.8s ease;
}

.wisdom-content-enter-from {
  opacity: 0;
  transform: translateY(12px);
}

.wisdom-content-enter-to {
  opacity: 1;
  transform: translateY(0);
}

.wisdom-screen__icon {
  display: flex;
  justify-content: center;
  margin-bottom: 16px;
  color: var(--tg-link-color, #2481cc);
  opacity: 0.5;
}

.wisdom-screen__text {
  font-family: 'Caveat', cursive;
  font-size: 26px;
  font-weight: 600;
  line-height: 1.35;
  color: var(--tg-text-color, #000);
  margin: 0;
}

.wisdom-screen__author {
  font-size: 14px;
  color: var(--tg-hint-color, #999);
  margin: 14px 0 0;
  font-style: normal;
}

.wisdom-screen__hint {
  position: absolute;
  bottom: 20px;
  left: 0;
  right: 0;
  text-align: center;
  font-size: 12px;
  color: var(--tg-hint-color, #999);
  opacity: 0.5;
  margin: 0;
  animation: wisdom-hint-in 1s ease 1.5s both;
}

@keyframes wisdom-hint-in {
  from { opacity: 0; }
  to { opacity: 0.5; }
}
</style>
