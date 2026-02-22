<script setup lang="ts">
withDefaults(defineProps<{
  lines?: number
  card?: boolean
}>(), {
  lines: 3,
  card: true
})
</script>

<template>
  <div class="skeleton" :class="{ 'skeleton--card': card }">
    <div
      v-for="i in lines"
      :key="i"
      class="skeleton__line"
      :style="{
        width: i === lines ? '55%' : i % 2 === 0 ? '75%' : '100%',
        animationDelay: `${(i - 1) * 0.1}s`
      }"
    />
  </div>
</template>

<style scoped>
.skeleton {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 16px;
  animation: dt-fade-in 0.2s ease;
}

.skeleton--card {
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border-radius: 14px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
}

.skeleton__line {
  height: 12px;
  border-radius: 6px;
  background: linear-gradient(
    90deg,
    var(--dt-skeleton-base, rgba(128,128,128,0.08)) 25%,
    var(--dt-skeleton-shimmer, rgba(128,128,128,0.16)) 50%,
    var(--dt-skeleton-base, rgba(128,128,128,0.08)) 75%
  );
  background-size: 200% 100%;
  animation: dt-shimmer 1.5s ease-in-out infinite;
}

@keyframes dt-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

@keyframes dt-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}
</style>
