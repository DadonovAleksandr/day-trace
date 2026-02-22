<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import type { UserSettings } from '../types'
import { getSettings, updateSettings } from '../api/settings'
import ErrorBanner from '../components/ErrorBanner.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'
import AppIcon from '../components/AppIcon.vue'

const settings = ref<UserSettings | null>(null)
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const success = ref<string | null>(null)

// Editable fields
const editTimezone = ref('')
const editReminderTime = ref('')
const editReminderEnabled = ref(true)
const editShowWisdom = ref(true)
const editWeekEnd = ref('')

// Cooldown / transition info
const cooldownRetryAfter = ref<number | null>(null)
const transitionInfo = ref<{ transition_start: string; transition_end: string; hint: string } | null>(null)

const weekDays = [
  { value: 'Sunday', label: 'Воскресенье' },
  { value: 'Monday', label: 'Понедельник' },
  { value: 'Tuesday', label: 'Вторник' },
  { value: 'Wednesday', label: 'Среда' },
  { value: 'Thursday', label: 'Четверг' },
  { value: 'Friday', label: 'Пятница' },
  { value: 'Saturday', label: 'Суббота' },
]

const commonTimezones = [
  'Europe/Moscow',
  'Europe/Kiev',
  'Europe/Minsk',
  'Asia/Almaty',
  'Asia/Tashkent',
  'Asia/Yekaterinburg',
  'Asia/Novosibirsk',
  'Asia/Krasnoyarsk',
  'Asia/Irkutsk',
  'Asia/Vladivostok',
  'Asia/Kamchatka',
  'Europe/London',
  'Europe/Berlin',
  'America/New_York',
  'America/Los_Angeles',
  'Asia/Tokyo',
  'Asia/Shanghai',
  'UTC',
]

const detectedTimezone = computed(() => {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone
  } catch {
    return null
  }
})

const hasChanges = computed(() => {
  if (!settings.value) return false
  return (
    editTimezone.value !== settings.value.timezone ||
    editReminderTime.value !== settings.value.reminder_time ||
    editReminderEnabled.value !== settings.value.reminder_enabled ||
    editShowWisdom.value !== settings.value.show_wisdom ||
    editWeekEnd.value !== settings.value.week_end
  )
})

const changedFields = computed(() => {
  if (!settings.value) return {}
  const changes: Partial<UserSettings> = {}
  if (editTimezone.value !== settings.value.timezone) changes.timezone = editTimezone.value
  if (editReminderTime.value !== settings.value.reminder_time) changes.reminder_time = editReminderTime.value
  if (editReminderEnabled.value !== settings.value.reminder_enabled) changes.reminder_enabled = editReminderEnabled.value
  if (editShowWisdom.value !== settings.value.show_wisdom) changes.show_wisdom = editShowWisdom.value
  if (editWeekEnd.value !== settings.value.week_end) changes.week_end = editWeekEnd.value
  return changes
})

function syncEditFields(s: UserSettings) {
  editTimezone.value = s.timezone
  editReminderTime.value = s.reminder_time
  editReminderEnabled.value = s.reminder_enabled
  editShowWisdom.value = s.show_wisdom
  editWeekEnd.value = s.week_end
}

async function fetchData() {
  loading.value = true
  error.value = null
  try {
    settings.value = await getSettings()
    syncEditFields(settings.value)
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить настройки'
  } finally {
    loading.value = false
  }
}

async function handleSave() {
  if (!hasChanges.value) return

  saving.value = true
  error.value = null
  success.value = null
  cooldownRetryAfter.value = null
  transitionInfo.value = null

  try {
    const result = await updateSettings(changedFields.value)
    settings.value = result
    syncEditFields(result)
    success.value = 'Настройки сохранены'
    setTimeout(() => { success.value = null }, 3000)
  } catch (err: any) {
    const status = err.response?.status
    const data = err.response?.data

    if (status === 429 && data?.error === 'timezone_change_cooldown') {
      cooldownRetryAfter.value = data.retry_after_seconds || null
      error.value = `Часовой пояс можно менять раз в 24 часа. ${cooldownRetryAfter.value ? 'Повторите через ' + formatSeconds(cooldownRetryAfter.value) : ''}`
      if (settings.value) editTimezone.value = settings.value.timezone
    } else if (status === 409 && data?.error === 'transition_pending') {
      transitionInfo.value = {
        transition_start: data.transition_start,
        transition_end: data.transition_end,
        hint: data.hint || '',
      }
      error.value = 'Предыдущая смена дня окончания недели ещё не завершена'
      if (settings.value) editWeekEnd.value = settings.value.week_end
    } else {
      error.value = data?.message || 'Не удалось сохранить настройки'
    }
  } finally {
    saving.value = false
  }
}

function handleReset() {
  if (settings.value) {
    syncEditFields(settings.value)
    error.value = null
    success.value = null
    cooldownRetryAfter.value = null
    transitionInfo.value = null
  }
}

function useDetectedTimezone() {
  if (detectedTimezone.value) {
    editTimezone.value = detectedTimezone.value
  }
}

function formatSeconds(s: number): string {
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  if (h > 0) return `${h}ч ${m}мин`
  return `${m}мин`
}

onMounted(fetchData)
</script>

<template>
  <div class="settings-view">
    <h2 class="view-title">Настройки</h2>

    <ErrorBanner v-if="error" :message="error" @dismiss="error = null" />
    <ErrorBanner v-if="success" :message="success" type="success" @dismiss="success = null" />

    <!-- Transition warning -->
    <ErrorBanner
      v-if="transitionInfo"
      :message="`Переходный период: ${transitionInfo.transition_start} — ${transitionInfo.transition_end}`"
      type="warning"
      @dismiss="transitionInfo = null"
    />

    <LoadingSkeleton v-if="loading" :lines="5" />

    <template v-else-if="settings">
      <!-- Timezone -->
      <div class="settings-group">
        <label class="group-label">
          <AppIcon name="globe" :size="16" />
          Часовой пояс
        </label>
        <div class="tz-row">
          <select v-model="editTimezone" class="input-field select-field">
            <option v-for="tz in commonTimezones" :key="tz" :value="tz">{{ tz }}</option>
            <option
              v-if="editTimezone && !commonTimezones.includes(editTimezone)"
              :value="editTimezone"
            >
              {{ editTimezone }}
            </option>
          </select>
          <button
            v-if="detectedTimezone && detectedTimezone !== editTimezone"
            class="btn-detect"
            @click="useDetectedTimezone"
            :title="'Использовать: ' + detectedTimezone"
          >
            <AppIcon name="map-pin" :size="14" />
            Авто
          </button>
        </div>
        <p class="hint">Текущий: {{ settings.timezone }}</p>
      </div>

      <!-- Reminder time -->
      <div class="settings-group">
        <label class="group-label">
          <AppIcon name="clock" :size="16" />
          Время напоминания
        </label>
        <input
          type="time"
          v-model="editReminderTime"
          class="input-field"
        />
        <p class="hint">Формат 24 часа (HH:mm)</p>
      </div>

      <!-- Reminder enabled -->
      <div class="settings-group toggle-group">
        <label class="group-label">
          <AppIcon name="bell" :size="16" />
          Напоминания
        </label>
        <label class="toggle">
          <input
            type="checkbox"
            v-model="editReminderEnabled"
          />
          <span class="toggle-slider"></span>
          <span class="toggle-text">{{ editReminderEnabled ? 'Включены' : 'Выключены' }}</span>
        </label>
      </div>

      <!-- Show wisdom -->
      <div class="settings-group toggle-group">
        <label class="group-label">
          <AppIcon name="book-open" :size="16" />
          Мудрость дня
        </label>
        <label class="toggle">
          <input type="checkbox" v-model="editShowWisdom" />
          <span class="toggle-slider"></span>
          <span class="toggle-text">{{ editShowWisdom ? 'Показывать' : 'Скрыта' }}</span>
        </label>
      </div>

      <!-- Week end day -->
      <div class="settings-group">
        <label class="group-label">
          <AppIcon name="calendar" :size="16" />
          День окончания недели
        </label>
        <select v-model="editWeekEnd" class="input-field select-field">
          <option v-for="day in weekDays" :key="day.value" :value="day.value">
            {{ day.label }}
          </option>
        </select>
        <p class="hint">Итог недели формируется в этот день</p>
      </div>

      <!-- Action buttons -->
      <div class="actions">
        <button
          class="btn btn--primary save-btn"
          :disabled="!hasChanges || saving"
          @click="handleSave"
        >
          <AppIcon :name="saving ? 'loader' : 'save'" :size="16" :class="{ 'spin': saving }" />
          {{ saving ? 'Сохраняем...' : 'Сохранить' }}
        </button>
        <button
          v-if="hasChanges"
          class="btn btn--secondary reset-btn"
          @click="handleReset"
        >
          <AppIcon name="undo" :size="16" />
          Сбросить
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.settings-view {
  max-width: 600px;
  margin: 0 auto;
}

.view-title {
  margin: 0 0 12px;
  font-size: 22px;
  font-weight: 700;
}

.settings-group {
  background: var(--tg-secondary-bg-color);
  border-radius: 14px;
  padding: 14px 16px;
  margin: 10px 0;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
}

.group-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 14px;
  font-weight: 600;
  margin-bottom: 10px;
  color: var(--tg-text-color);
}

.input-field {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.1));
  border-radius: 10px;
  font-size: 14px;
  background: var(--tg-bg-color, #fff);
  color: var(--tg-text-color, #000);
  box-sizing: border-box;
  font-family: inherit;
  transition: border-color 200ms ease;
}

.input-field:focus {
  outline: none;
  border-color: var(--tg-button-color, #2481cc);
}

.select-field {
  appearance: auto;
}

.tz-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.tz-row .input-field {
  flex: 1;
}

.btn-detect {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 10px 12px;
  border: 1px solid var(--tg-button-color, #3390ec);
  border-radius: 10px;
  background: transparent;
  color: var(--tg-button-color, #3390ec);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  white-space: nowrap;
  transition: all 200ms ease;
}

.btn-detect:hover {
  background: var(--tg-button-color, #3390ec);
  color: var(--tg-button-text-color, #fff);
}

.hint {
  font-size: 12px;
  color: var(--tg-hint-color);
  margin: 6px 0 0;
}

/* Toggle switch */
.toggle-group {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.toggle {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.toggle input {
  display: none;
}

.toggle-slider {
  width: 44px;
  height: 24px;
  background: var(--tg-hint-color, #ccc);
  border-radius: 12px;
  position: relative;
  transition: background 0.25s ease;
}

.toggle-slider::after {
  content: '';
  width: 20px;
  height: 20px;
  background: white;
  border-radius: 50%;
  position: absolute;
  top: 2px;
  left: 2px;
  transition: transform 0.25s ease;
  box-shadow: 0 1px 3px rgba(0,0,0,0.15);
}

.toggle input:checked + .toggle-slider {
  background: var(--tg-button-color, #3390ec);
}

.toggle input:checked + .toggle-slider::after {
  transform: translateX(20px);
}

.toggle-text {
  font-size: 13px;
  color: var(--tg-hint-color);
}

/* Action buttons */
.actions {
  display: flex;
  gap: 8px;
  margin: 16px 0;
}

.btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 12px 18px;
  border: none;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 200ms ease;
}

.btn:active {
  transform: scale(0.97);
}

.btn--primary {
  background: var(--tg-button-color, #3390ec);
  color: var(--tg-button-text-color, #fff);
}

.btn--primary:disabled {
  opacity: 0.4;
  cursor: default;
}

.btn--primary:hover:not(:disabled) {
  filter: brightness(1.08);
}

.btn--secondary {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.12));
}

.save-btn {
  flex: 1;
}

.spin {
  animation: dt-spin 1s linear infinite;
}

@keyframes dt-spin {
  to { transform: rotate(360deg); }
}
</style>
