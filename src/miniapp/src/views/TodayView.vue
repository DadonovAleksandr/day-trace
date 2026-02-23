<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents, createEvent } from '../api/events'
import { getSummaries } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { isEventLocked } from '../composables/useLockCheck'
import { useSettingsStore } from '../stores/settings'
import StarPicker from '../components/StarPicker.vue'
import SatisfactionPicker from '../components/SatisfactionPicker.vue'
import { getDayRating, setDayRating } from '../api/dayRating'
import ErrorBanner from '../components/ErrorBanner.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'
import PeriodNav from '../components/PeriodNav.vue'
import AppIcon from '../components/AppIcon.vue'

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

// New event form
const newText = ref('')
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

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10)
}

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
    newText.value = ''
    newImportance.value = 3
    await fetchEvents()
  } catch (err: any) {
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

watch(dayOffset, fetchEvents)
onMounted(() => {
  fetchEvents()
  settingsStore.fetchSettings()
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
            <AppIcon name="calendar" :size="14" />
            Сегодня
          </button>
        </Transition>
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
          <div class="write-area">
            <textarea
              v-model="newText"
              :placeholder="isToday ? 'Что важного произошло сегодня?' : 'Что произошло в этот день?'"
              maxlength="500"
              rows="8"
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

          <button
            class="save-btn"
            :disabled="!newText.trim() || newText.length > 500 || submitting"
            @click="handleCreate"
          >
            {{ submitting ? 'Сохраняем...' : 'Записать' }}
          </button>
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
            <div v-if="deletingId === currentEvent.id" class="delete-bar">
              <span class="delete-bar__text">Удалить запись?</span>
              <div class="delete-bar__actions">
                <button class="btn-sm btn-sm--ghost" @click="deletingId = null">Нет</button>
                <button
                  class="btn-sm btn-sm--danger"
                  :disabled="editSubmitting"
                  @click="handleDelete(currentEvent.id)"
                >
                  {{ editSubmitting ? '...' : 'Да' }}
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
              rows="5"
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
  gap: 6px;
  margin-left: 8px;
  padding: 5px 14px;
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
  border: none;
  border-radius: 20px;
  font-size: 13px;
  font-weight: 600;
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
  padding: 0 2px;
  margin-bottom: 14px;
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
   Delete Confirmation Bar
   ======================================== */
.delete-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 12px;
  padding: 10px 14px;
  background: var(--dt-warning-bg, rgba(255, 152, 0, 0.08));
  border: 1px solid var(--dt-warning-border, rgba(255, 152, 0, 0.16));
  border-radius: 12px;
}

.delete-bar__text {
  font-size: 14px;
  font-weight: 500;
  color: var(--tg-text-color);
}

.delete-bar__actions {
  display: flex;
  gap: 8px;
}

.btn-sm {
  padding: 6px 14px;
  border: none;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
  transition: all 200ms ease;
}

.btn-sm:active {
  transform: scale(0.95);
}

.btn-sm--ghost {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0, 0, 0, 0.1));
}

.btn-sm--danger {
  background: var(--dt-error-text, #e53935);
  color: #fff;
}

.btn-sm--danger:disabled {
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
