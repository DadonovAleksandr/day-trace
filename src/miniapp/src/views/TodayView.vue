<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents, createEvent } from '../api/events'
import { getSummaries } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { isEventLocked } from '../composables/useLockCheck'
import { useSettingsStore } from '../stores/settings'
import EventCard from '../components/EventCard.vue'
import StarPicker from '../components/StarPicker.vue'
import SatisfactionPicker from '../components/SatisfactionPicker.vue'
import { getDayRating, setDayRating } from '../api/dayRating'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
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
const showForm = ref(false)
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

const sortedEvents = computed(() =>
  [...events.value].sort((a, b) => b.importance - a.importance)
)

const textCharCount = computed(() => newText.value.length)
const editTextCharCount = computed(() => editText.value.length)

function getEventLock(evt: EventItem) {
  return isEventLocked(evt.local_date, weeklySummaries.value)
}

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10)
}

async function fetchEvents() {
  loading.value = true
  error.value = null
  try {
    const dateStr = toDateStr(selectedDate.value)

    // Определяем даты недели выбранного дня для загрузки weekly summaries
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
    showForm.value = false
    await fetchEvents()
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось создать событие'
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
  <div class="today-view">
    <div class="header">
      <h2 class="header__title">{{ headerTitle }}</h2>
      <PeriodNav
        :label="dayLabel"
        :can-go-forward="dayOffset < 0"
        @prev="dayOffset--"
        @next="dayOffset++"
      />
      <Transition name="today-btn">
        <button v-if="!isToday" class="back-today-btn" @click="dayOffset = 0">
          <AppIcon name="calendar" :size="14" />
          Сегодня
        </button>
      </Transition>
    </div>

    <ErrorBanner v-if="error || editError" :message="error || editError || ''" @dismiss="error = null; editError && (editError = null)" />

    <!-- Add event button -->
    <button v-if="!showForm" class="add-btn" @click="showForm = true">
      <AppIcon name="plus" :size="18" />
      Добавить событие
    </button>

    <!-- Create event form -->
    <Transition name="form">
      <div v-if="showForm" class="event-form">
        <div class="form-field">
          <textarea
            v-model="newText"
            placeholder="Что произошло?"
            maxlength="500"
            rows="3"
            class="form-textarea"
          ></textarea>
          <span class="char-count" :class="{ 'char-count--warn': textCharCount > 450 }">
            {{ textCharCount }}/500
          </span>
        </div>

        <div v-if="importanceEnabled" class="form-field">
          <label class="form-label">Важность</label>
          <StarPicker v-model="newImportance" />
        </div>

        <div class="form-actions">
          <button class="btn btn--secondary" @click="showForm = false">Отмена</button>
          <button
            class="btn btn--primary"
            :disabled="!newText.trim() || newText.length > 500 || submitting"
            @click="handleCreate"
          >
            {{ submitting ? 'Сохраняем...' : 'Сохранить' }}
          </button>
        </div>
      </div>
    </Transition>

    <!-- Loading -->
    <LoadingSkeleton v-if="loading" :lines="4" />

    <!-- Empty state -->
    <EmptyState
      v-else-if="!events.length"
      message="Событий пока нет. Добавьте первое!"
      icon="today"
    />

    <!-- Events list -->
    <div v-else class="events-list">
      <div v-for="evt in sortedEvents" :key="evt.id">
        <!-- Edit mode -->
        <div v-if="editingId === evt.id" class="event-form event-form--inline">
          <div class="form-field">
            <textarea v-model="editText" maxlength="500" rows="2" class="form-textarea"></textarea>
            <span class="char-count" :class="{ 'char-count--warn': editTextCharCount > 450 }">
              {{ editTextCharCount }}/500
            </span>
          </div>
          <div v-if="importanceEnabled" class="form-field">
            <StarPicker v-model="editImportance" />
          </div>
          <div class="form-actions">
            <button class="btn btn--secondary" @click="cancelEdit">Отмена</button>
            <button
              class="btn btn--primary"
              :disabled="!editText.trim() || editText.length > 500 || editSubmitting"
              @click="handleEdit(evt.id)"
            >
              {{ editSubmitting ? 'Сохраняем...' : 'Сохранить' }}
            </button>
          </div>
        </div>

        <!-- Display mode -->
        <template v-else>
          <EventCard
            :event="evt"
            :editable="true"
            :locked="getEventLock(evt).locked"
            :lock-reason="getEventLock(evt).reason"
            :show-importance="importanceEnabled"
            @edit="startEdit"
            @delete="deletingId = $event.id"
          />

          <!-- Delete confirmation -->
          <Transition name="form">
            <div v-if="deletingId === evt.id" class="delete-confirm">
              <p>Удалить событие?</p>
              <div class="form-actions">
                <button class="btn btn--secondary" @click="deletingId = null">Нет</button>
                <button class="btn btn--danger" :disabled="editSubmitting" @click="handleDelete(evt.id)">
                  {{ editSubmitting ? '...' : 'Да, удалить' }}
                </button>
              </div>
            </div>
          </Transition>
        </template>
      </div>
    </div>

    <!-- Day satisfaction -->
    <div v-if="satisfactionEnabled && !loading" class="satisfaction-section">
      <div class="satisfaction-card">
        <span class="satisfaction-label">Как прошёл день?</span>
        <SatisfactionPicker
          :model-value="daySatisfaction"
          @update:model-value="handleSatisfaction"
          :class="{ 'satisfaction-saving': satisfactionSaving }"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.today-view {
  max-width: 600px;
  margin: 0 auto;
}

.header__title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  text-align: center;
}

/* Back to today */
.back-today-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin: 4px auto 0;
  padding: 6px 16px;
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
  border: none;
  border-radius: 20px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: all 200ms ease;
}

.back-today-btn:active {
  transform: scale(0.95);
}

.today-btn-enter-active,
.today-btn-leave-active {
  transition: all 0.2s ease;
}

.today-btn-enter-from,
.today-btn-leave-to {
  opacity: 0;
  transform: translateY(-6px) scale(0.9);
}

.add-btn {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 12px;
  margin: 16px 0;
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
  border: none;
  border-radius: 12px;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
  transition: all 200ms ease;
}

.add-btn:hover {
  filter: brightness(1.08);
}

.add-btn:active {
  transform: scale(0.98);
}

/* Form */
.event-form {
  background: var(--tg-secondary-bg-color);
  border-radius: 14px;
  padding: 14px;
  margin: 12px 0;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
}

.event-form--inline {
  margin: 0 0 8px;
}

.form-field {
  margin-bottom: 12px;
  position: relative;
}

.form-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--tg-hint-color);
  margin-bottom: 6px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.form-textarea,
.form-input {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.1));
  border-radius: 10px;
  font-size: 14px;
  background: var(--tg-bg-color);
  color: var(--tg-text-color);
  resize: none;
  transition: border-color 200ms ease;
  font-family: inherit;
}

.form-textarea:focus,
.form-input:focus {
  outline: none;
  border-color: var(--tg-button-color, #2481cc);
}

.char-count {
  position: absolute;
  right: 10px;
  bottom: 6px;
  font-size: 11px;
  color: var(--tg-hint-color);
}

.char-count--warn {
  color: var(--dt-error-text, #e53935);
}

.form-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  margin-top: 8px;
}

.btn {
  padding: 8px 18px;
  border: none;
  border-radius: 9px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 200ms ease;
}

.btn:active {
  transform: scale(0.97);
}

.btn--primary {
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.btn--primary:disabled {
  opacity: 0.45;
  cursor: default;
}

.btn--secondary {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.12));
}

.btn--danger {
  background: var(--dt-error-text, #e53935);
  color: #fff;
}

/* Events list */
.events-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

/* Delete confirmation */
.delete-confirm {
  width: 100%;
  background: var(--dt-warning-bg, rgba(255,152,0,0.08));
  border: 1px solid var(--dt-warning-border, rgba(255,152,0,0.16));
  border-radius: 10px;
  padding: 10px 12px;
  margin-top: 4px;
}

.delete-confirm p {
  margin-bottom: 8px;
  font-size: 13px;
  font-weight: 500;
}

/* Form transition */
.form-enter-active,
.form-leave-active {
  transition: all 0.2s ease;
}

.form-enter-from,
.form-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}

/* Day satisfaction */
.satisfaction-section {
  margin-top: 16px;
}

.satisfaction-card {
  background: var(--tg-secondary-bg-color);
  border-radius: 14px;
  padding: 14px 16px;
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}

.satisfaction-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--tg-text-color);
}

.satisfaction-saving {
  opacity: 0.5;
  pointer-events: none;
}
</style>
