<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents, createEvent } from '../api/events'
import { getSummaries } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { useDraftSave } from '../composables/useDraftSave'
import { isEventLocked } from '../composables/useLockCheck'
import { useTelegram } from '../composables/useTelegram'
import { useHaptic } from '../composables/useHaptic'
import { useKeyboardHeight } from '../composables/useKeyboardHeight'
import { useSettingsStore } from '../stores/settings'
import StarPicker from '../components/StarPicker.vue'
import SatisfactionPicker from '../components/SatisfactionPicker.vue'
import { getDayRating, setDayRating } from '../api/dayRating'
import ErrorBanner from '../components/ErrorBanner.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'
import PeriodNav from '../components/PeriodNav.vue'
import AppIcon from '../components/AppIcon.vue'

const { showBackButton, hideBackButton } = useTelegram()
const { notification: hapticNotification, impact: hapticImpact } = useHaptic()
const { isKeyboardVisible } = useKeyboardHeight()

const settingsStore = useSettingsStore()
const importanceEnabled = computed(() => settingsStore.settings?.importance_enabled ?? true)
const satisfactionEnabled = computed(() => settingsStore.settings?.satisfaction_enabled ?? true)

const events = ref<EventItem[]>([])
const weeklySummaries = ref<Summary[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

// Day navigation
const dayOffset = ref(0)

const selectedDate = computed(() => {
  const d = new Date()
  d.setDate(d.getDate() + dayOffset.value)
  return d
})

const isToday = computed(() => dayOffset.value === 0)

const dayLabel = computed(() => {
  return selectedDate.value.toLocaleDateString('ru-RU', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })
})

const headerTitle = computed(() => isToday.value ? 'Сегодня' : 'День')

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10)
}

// Draft auto-save
const currentDateStr = computed(() => toDateStr(selectedDate.value))
const { draftText: newText, clearDraft } = useDraftSave(currentDateStr)

// New event form
const newImportance = ref(3)
const submitting = ref(false)
const daySatisfaction = ref<number | null>(null)
const satisfactionSaving = ref(false)

const {
  editingId,
  editText,
  editImportance,
  deletingId,
  submitting: editSubmitting,
  editError,
  startEdit,
  cancelEdit,
  handleEdit,
  handleDelete,
} = useEventEditing(fetchEvents, { importanceEnabled: () => importanceEnabled.value })

const currentEvent = computed(() => events.value.length > 0 ? events.value[0] : null)

const currentEventLock = computed(() => {
  if (!currentEvent.value) return { locked: false, reason: '' }
  return isEventLocked(currentEvent.value.local_date, weeklySummaries.value)
})

const showCreateForm = computed(() => !loading.value && !currentEvent.value)

const textCharCount = computed(() => newText.value.length)
const editTextCharCount = computed(() => editText.value.length)

async function fetchEvents() {
  loading.value = true
  error.value = null
  try {
    const dateStr = toDateStr(selectedDate.value)

    const target = selectedDate.value
    const day = target.getDay()
    const weekStart = new Date(target)
    weekStart.setDate(target.getDate() - ((day === 0 ? 7 : day) - 1))
    const weekEnd = new Date(weekStart)
    weekEnd.setDate(weekStart.getDate() + 6)

    const [eventsRes, summariesRes, ratingRes] = await Promise.all([
      getEvents({ from: dateStr, to: dateStr }),
      getSummaries('weekly', { from: toDateStr(weekStart), to: toDateStr(weekEnd), limit: 10 }),
      getDayRating(dateStr),
    ])
    events.value = eventsRes.items
    weeklySummaries.value = summariesRes.items
    daySatisfaction.value = ratingRes.rating
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить события'
  } finally {
    loading.value = false
  }
}

async function handleCreate() {
  if (!newText.value.trim() || newText.value.length > 500) return
  submitting.value = true
  try {
    await createEvent({
      text: newText.value.trim(),
      importance: importanceEnabled.value ? newImportance.value : 3,
      local_date: toDateStr(selectedDate.value),
    })
    hapticNotification('success')
    clearDraft()
    newImportance.value = 3
    await fetchEvents()
  } catch (err: any) {
    hapticNotification('error')
    if (err.response?.status === 409) {
      await fetchEvents()
      error.value = 'Событие на этот день уже существует. Вы можете его отредактировать.'
    } else {
      error.value = err.response?.data?.message || 'Не удалось создать событие'
    }
  } finally {
    submitting.value = false
  }
}

async function handleSatisfaction(value: number) {
  satisfactionSaving.value = true
  try {
    const result = await setDayRating({
      rating: value,
      local_date: toDateStr(selectedDate.value),
    })
    daySatisfaction.value = result.rating
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось сохранить оценку дня'
  } finally {
    satisfactionSaving.value = false
  }
}

// BackButton: show when entering edit mode, hide when leaving
watch(editingId, (newId) => {
  if (newId !== null) {
    showBackButton(cancelEdit)
  } else {
    hideBackButton()
  }
})

// Haptic feedback when delete sheet appears
watch(deletingId, (newVal, oldVal) => {
  if (oldVal === null && newVal !== null) {
    hapticImpact('medium')
  }
})

watch(dayOffset, fetchEvents)
onMounted(() => {
  fetchEvents()
  settingsStore.fetchSettings()
})
onUnmounted(() => {
  hideBackButton()
})
</script>

<template>
  <div class="journal">
    <!-- Header -->
    <header class="journal-header">
      <h2 class="journal-title">{{ headerTitle }}</h2>
      <div class="journal-nav-row">
        <PeriodNav
          :label="dayLabel"
          :can-go-forward="dayOffset < 0"
          @prev="dayOffset--"
          @next="dayOffset++"
        />
        <Transition name="fade-slide">
          <button v-if="!isToday" class="back-today-btn" @click="dayOffset = 0">
            <AppIcon name="today" :size="16" />
          </button>
        </Transition>
      </div>
      <div v-if="!loading" class="day-status">
        <span :class="['day-status__dot', currentEvent ? 'day-status__dot--done' : '']"></span>
        <span class="day-status__text">{{ currentEvent ? 'Записано' : 'Ещё нет записи' }}</span>
      </div>
    </header>

    <ErrorBanner
      v-if="error || editError"
      :message="error || editError || ''"
      @dismiss="error = null; editError && (editError = null)"
    />

    <!-- Loading -->
    <div v-if="loading" class="journal-body">
      <LoadingSkeleton :lines="5" />
    </div>

    <!-- Journal content — state machine with transitions -->
    <template v-else>
      <Transition name="entry" mode="out-in">
        <!-- State: Create (no event for this day) -->
        <section v-if="showCreateForm" key="create" class="journal-body">
          <div class="write-prompt">
            <AppIcon name="sparkles" :size="16" />
            <span>Запишите ключевое событие дня</span>
          </div>
          <div class="write-area">
            <textarea
              v-model="newText"
              :placeholder="isToday ? 'Что важного произошло сегодня?' : 'Что произошло в этот день?'"
              maxlength="500"
              rows="6"
              class="write-textarea"
            ></textarea>
            <span
              class="write-charcount"
              :class="{ 'write-charcount--warn': textCharCount > 450 }"
            >{{ textCharCount }}/500</span>
          </div>

          <div v-if="importanceEnabled" class="meta-row">
            <span class="meta-label">Важность</span>
            <StarPicker v-model="newImportance" />
          </div>

          <div v-if="satisfactionEnabled" class="meta-row">
            <span class="meta-label">День</span>
            <SatisfactionPicker
              :model-value="daySatisfaction"
              @update:model-value="handleSatisfaction"
              :class="{ 'mood--saving': satisfactionSaving }"
            />
          </div>

          <div class="sticky-actions" :style="isKeyboardVisible ? { bottom: 'var(--dt-keyboard-height)' } : {}">
            <button
              class="save-btn"
              :disabled="!newText.trim() || newText.length > 500 || submitting"
              @click="handleCreate"
            >
              {{ submitting ? 'Сохраняем...' : 'Записать' }}
            </button>
          </div>
        </section>

        <!-- State: Display (event exists, not editing) -->
        <section
          v-else-if="currentEvent && editingId !== currentEvent.id"
          key="display"
          class="journal-body"
        >
          <article class="entry-display">
            <p class="entry-text">{{ currentEvent.text }}</p>
          </article>

          <div class="entry-meta-display">
            <div v-if="importanceEnabled" class="meta-row meta-row--readonly">
              <span class="meta-label">Важность</span>
              <StarPicker
                :model-value="currentEvent.importance"
                readonly
                size="sm"
              />
            </div>
            <div v-if="satisfactionEnabled" class="meta-row meta-row--readonly">
              <span class="meta-label">День</span>
              <SatisfactionPicker
                :model-value="daySatisfaction"
                @update:model-value="handleSatisfaction"
                :class="{ 'mood--saving': satisfactionSaving }"
                size="sm"
              />
            </div>
          </div>

          <div class="entry-footer">
            <div v-if="!currentEventLock.locked" class="entry-actions">
              <button
                class="btn-action btn-action--ghost"
                @click="startEdit(currentEvent)"
              >
                <AppIcon name="edit" :size="15" />
                Редактировать
              </button>
              <button
                class="btn-action btn-action--danger-ghost"
                @click="deletingId = currentEvent.id"
              >
                <AppIcon name="trash" :size="15" />
                Удалить
              </button>
            </div>
            <div v-else class="entry-locked">
              <AppIcon name="lock" :size="13" />
              <span>{{ currentEventLock.reason }}</span>
            </div>
          </div>

          <!-- Delete confirmation -->
          <Transition name="slide-up">
            <div v-if="deletingId === currentEvent.id" class="delete-sheet">
              <div class="delete-sheet__icon">
                <AppIcon name="trash" :size="24" />
              </div>
              <h3 class="delete-sheet__title">Удалить запись?</h3>
              <p class="delete-sheet__subtitle">Это действие нельзя отменить</p>
              <div class="delete-sheet__actions">
                <button class="delete-sheet__btn delete-sheet__btn--cancel" @click="deletingId = null">Отмена</button>
                <button
                  class="delete-sheet__btn delete-sheet__btn--confirm"
                  :disabled="editSubmitting"
                  @click="handleDelete(currentEvent.id)"
                >
                  {{ editSubmitting ? '...' : 'Удалить' }}
                </button>
              </div>
            </div>
          </Transition>
        </section>

        <!-- State: Edit -->
        <section
          v-else-if="currentEvent && editingId === currentEvent.id"
          key="edit"
          class="journal-body"
        >
          <div class="write-area">
            <textarea
              v-model="editText"
              maxlength="500"
              rows="6"
              class="write-textarea"
            ></textarea>
            <span
              class="write-charcount"
              :class="{ 'write-charcount--warn': editTextCharCount > 450 }"
            >{{ editTextCharCount }}/500</span>
          </div>

          <div v-if="importanceEnabled" class="meta-row">
            <span class="meta-label">Важность</span>
            <StarPicker v-model="editImportance" />
          </div>

          <div v-if="satisfactionEnabled" class="meta-row">
            <span class="meta-label">День</span>
            <SatisfactionPicker
              :model-value="daySatisfaction"
              @update:model-value="handleSatisfaction"
              :class="{ 'mood--saving': satisfactionSaving }"
            />
          </div>

          <div class="sticky-actions" :style="isKeyboardVisible ? { bottom: 'var(--dt-keyboard-height)' } : {}">
            <div class="edit-actions">
              <button class="btn-action btn-action--ghost" @click="cancelEdit">Отмена</button>
              <button
                class="btn-action btn-action--primary"
                :disabled="!editText.trim() || editText.length > 500 || editSubmitting"
                @click="handleEdit(currentEvent.id)"
              >
                {{ editSubmitting ? 'Сохраняем...' : 'Сохранить' }}
              </button>
            </div>
          </div>
        </section>
      </Transition>

    </template>
  </div>
</template>

<style scoped>
/* ========================================
   Journal Page Layout
   ======================================== */
.journal {
  max-width: 600px;
  margin: 0 auto;
  padding: 0 4px;
  min-height: 60vh;
  display: flex;
  flex-direction: column;
}

/* ========================================
   Header
   ======================================== */
.journal-header {
  text-align: center;
  flex-shrink: 0;
}

.journal-title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--tg-text-color);
}

.journal-nav-row {
  display: flex;
  align-items: center;
  justify-content: center;
}

.back-today-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  margin-left: 8px;
  padding: 6px;
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
  border: none;
  border-radius: 50%;
  cursor: pointer;
  flex-shrink: 0;
  transition: transform 200ms ease;
}

.back-today-btn:active {
  transform: scale(0.95);
}

/* ========================================
   Journal Body — the "page"
   ======================================== */
.journal-body {
  flex: 1;
  padding: 20px 4px 8px;
}

/* ========================================
   Write Area (Create & Edit)
   ======================================== */
.write-area {
  position: relative;
  margin-bottom: 16px;
}

.write-textarea {
  width: 100%;
  min-height: 140px;
  padding: 14px 16px 28px;
  border: 1.5px dashed var(--dt-card-border, rgba(0, 0, 0, 0.12));
  border-radius: 14px;
  font-size: 15px;
  line-height: 1.65;
  background: transparent;
  color: var(--tg-text-color);
  resize: none;
  font-family: inherit;
  transition: border-color 250ms ease, border-style 250ms ease;
}

.write-textarea::placeholder {
  color: var(--tg-hint-color);
  opacity: 0.7;
}

.write-textarea:focus {
  outline: none;
  border-color: var(--tg-button-color);
  border-style: solid;
}

.write-charcount {
  position: absolute;
  right: 12px;
  bottom: 8px;
  font-size: 11px;
  color: var(--tg-hint-color);
  pointer-events: none;
  transition: color 200ms ease;
}

.write-charcount--warn {
  color: var(--dt-error-text, #e53935);
}

/* ========================================
   Meta Row (importance label + stars)
   ======================================== */
.meta-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
  padding: 0 2px;
}

.meta-label {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--tg-hint-color);
}

/* ========================================
   Sticky Actions
   ======================================== */
.sticky-actions {
  position: sticky;
  bottom: 0;
  padding: 12px 0;
  background: var(--tg-bg-color, #ffffff);
  z-index: 10;
}

.sticky-actions::before {
  content: '';
  position: absolute;
  top: -12px;
  left: -16px;
  right: -16px;
  height: 12px;
  background: linear-gradient(to top, var(--tg-bg-color, #ffffff), transparent);
  pointer-events: none;
}

/* ========================================
   Save / Create Button
   ======================================== */
.save-btn {
  display: block;
  width: 100%;
  padding: 12px 20px;
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
  border: none;
  border-radius: 12px;
  font-size: 15px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: opacity 200ms ease, transform 200ms ease;
}

.save-btn:active:not(:disabled) {
  transform: scale(0.98);
}

.save-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

/* ========================================
   Entry Display (filled state)
   ======================================== */
.entry-display {
  padding: 16px;
  margin-bottom: 14px;
  background: var(--tg-secondary-bg-color, #f5f5f5);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.05));
  border-radius: 14px;
}

.entry-text {
  margin: 0;
  font-size: 16px;
  line-height: 1.7;
  word-break: break-word;
  white-space: pre-wrap;
  color: var(--tg-text-color);
}

/* ========================================
   Entry Footer (actions)
   ======================================== */
.entry-footer {
  padding: 0 2px;
}

.entry-actions {
  display: flex;
  gap: 8px;
}

/* ========================================
   Locked State
   ======================================== */
.entry-locked {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--tg-hint-color);
  opacity: 0.7;
}

/* ========================================
   Delete Confirmation Sheet
   ======================================== */
.delete-sheet {
  display: flex;
  flex-direction: column;
  align-items: center;
  margin-top: 16px;
  padding: 20px;
  background: var(--dt-error-bg, rgba(239, 83, 80, 0.07));
  border: 1.5px solid var(--dt-error-border, rgba(239, 83, 80, 0.16));
  border-radius: 16px;
}

.delete-sheet__icon {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  background: var(--dt-error-border, rgba(239, 83, 80, 0.16));
  color: var(--dt-error-text, #e53935);
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 10px;
}

.delete-sheet__title {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
  color: var(--tg-text-color);
}

.delete-sheet__subtitle {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--tg-hint-color, #999);
}

.delete-sheet__actions {
  display: flex;
  gap: 10px;
  margin-top: 16px;
  width: 100%;
}

.delete-sheet__btn {
  flex: 1;
  padding: 10px 16px;
  border: none;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: all 200ms ease;
}

.delete-sheet__btn:active {
  transform: scale(0.97);
}

.delete-sheet__btn--cancel {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.1));
}

.delete-sheet__btn--confirm {
  background: var(--dt-error-text, #e53935);
  color: #fff;
}

.delete-sheet__btn--confirm:disabled {
  opacity: 0.5;
}

/* ========================================
   Edit Actions
   ======================================== */
.edit-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}

.btn-action {
  padding: 10px 20px;
  border: none;
  border-radius: 10px;
  font-size: 14px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: all 200ms ease;
}

.btn-action:active:not(:disabled) {
  transform: scale(0.97);
}

.btn-action--ghost {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.1));
}

.btn-action--danger-ghost {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: transparent;
  color: var(--dt-error-text, #e53935);
  border: 1px solid var(--dt-error-text, rgba(229, 57, 53, 0.25));
}

.btn-action--primary {
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.btn-action--primary:disabled {
  opacity: 0.4;
  cursor: default;
}

/* ========================================
   Entry Meta Display (readonly stars + mood)
   ======================================== */
.entry-meta-display {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 0 2px;
  margin-bottom: 14px;
}

.meta-row--readonly {
  margin-bottom: 0;
}

.mood--saving {
  opacity: 0.45;
  pointer-events: none;
}

/* ========================================
   Day Status Indicator
   ======================================== */
.day-status {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin-top: 6px;
}

.day-status__dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--tg-hint-color, #999);
}

.day-status__dot--done {
  background: var(--dt-success-text, #388e3c);
}

.day-status__text {
  font-size: 12px;
  color: var(--tg-hint-color, #999);
}

/* ========================================
   Write Prompt
   ======================================== */
.write-prompt {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  font-size: 13px;
  color: var(--tg-hint-color, #999);
}

/* ========================================
   Transitions
   ======================================== */

/* Entry state transitions (create ↔ display ↔ edit) */
.entry-enter-active,
.entry-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.entry-enter-from {
  opacity: 0;
  transform: translateY(6px);
}

.entry-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}

/* Back-to-today button */
.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: all 0.2s ease;
}

.fade-slide-enter-from,
.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-6px) scale(0.9);
}

/* Delete confirmation bar */
.slide-up-enter-active,
.slide-up-leave-active {
  transition: all 0.2s ease;
}

.slide-up-enter-from,
.slide-up-leave-to {
  opacity: 0;
  transform: translateY(6px);
}

/* Generic fade */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
