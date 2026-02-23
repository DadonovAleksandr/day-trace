<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries, setHighlight } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { isEventLocked, isSummaryLocked } from '../composables/useLockCheck'
import { useSettingsStore } from '../stores/settings'
import EventCard from '../components/EventCard.vue'
import StarPicker from '../components/StarPicker.vue'
import PeriodNav from '../components/PeriodNav.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const events = ref<EventItem[]>([])
const summary = ref<Summary | null>(null)
const weeklySummaries = ref<Summary[]>([])
const yearlySummaries = ref<Summary[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const monthOffset = ref(0)
const selectedEventId = ref<string | null>(null)
const isSelecting = ref(false)
const settingsStore = useSettingsStore()

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
} = useEventEditing(fetchData)

const editTextCharCount = computed(() => editText.value.length)

const monthRange = computed(() => {
  const now = new Date()
  const year = now.getFullYear()
  const month = now.getMonth() + monthOffset.value

  const start = new Date(year, month, 1)
  const end = new Date(start.getFullYear(), start.getMonth() + 1, 0)

  return {
    start,
    end,
    startStr: formatDateISO(start),
    endStr: formatDateISO(end),
  }
})

const monthLabel = computed(() => {
  const { start } = monthRange.value
  return start.toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' })
})

const groupedEvents = computed(() => {
  const groups: Record<string, EventItem[]> = {}
  for (const evt of events.value) {
    if (!groups[evt.local_date]) groups[evt.local_date] = []
    groups[evt.local_date]!.push(evt)
  }
  const sorted = Object.entries(groups).sort(([a], [b]) => a.localeCompare(b))
  return sorted.map(([date, items]) => ({
    date,
    dateLabel: new Date(date + 'T00:00:00').toLocaleDateString('ru-RU', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
    }),
    events: items.sort((a, b) => b.importance - a.importance),
  }))
})

const importanceCounts = computed(() => {
  const counts = [0, 0, 0, 0, 0]
  for (const evt of events.value) {
    if (evt.importance >= 1 && evt.importance <= 5) {
      counts[evt.importance - 1] = (counts[evt.importance - 1] ?? 0) + 1
    }
  }
  return counts
})

const summaryLock = computed(() => {
  return isSummaryLocked('monthly', monthRange.value.startStr, monthRange.value.endStr, yearlySummaries.value)
})

const hasSavedHighlight = computed(() => {
  return summary.value !== null && summary.value.highlight_event_id !== null
})

const hasUnsavedChanges = computed(() => {
  if (!isSelecting.value) return false
  if (!selectedEventId.value) return false
  return selectedEventId.value !== summary.value?.highlight_event_id
})

function getEventLock(evt: EventItem) {
  return isEventLocked(evt.local_date, weeklySummaries.value)
}

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

function selectEvent(eventId: string) {
  if (!isSelecting.value) return
  selectedEventId.value = selectedEventId.value === eventId ? null : eventId
}

function enterSelectionMode() {
  if (summaryLock.value.locked) return
  cancelEdit()
  deletingId.value = null
  isSelecting.value = true
  selectedEventId.value = summary.value?.highlight_event_id ?? null
}

function cancelSelection() {
  isSelecting.value = false
  selectedEventId.value = summary.value?.highlight_event_id ?? null
}

async function saveHighlight() {
  if (!selectedEventId.value) return
  saving.value = true
  error.value = null
  try {
    const result = await setHighlight('monthly', {
      event_id: selectedEventId.value,
      period_start: monthRange.value.startStr,
      period_end: monthRange.value.endStr,
    })
    summary.value = result
    isSelecting.value = false
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось сохранить выбор'
  } finally {
    saving.value = false
  }
}

async function fetchData() {
  loading.value = true
  error.value = null
  isSelecting.value = false
  selectedEventId.value = null
  deletingId.value = null
  cancelEdit()
  try {
    const { startStr, endStr } = monthRange.value
    const yearStart = new Date(monthRange.value.start.getFullYear(), 0, 1)
    const yearEnd = new Date(monthRange.value.start.getFullYear(), 11, 31)

    const [eventsRes, weeklyRes, monthlyRes, yearlyRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('weekly', { from: startStr, to: endStr, limit: 20 }),
      getSummaries('monthly', { from: startStr, to: endStr, limit: 1 }),
      getSummaries('yearly', {
        from: formatDateISO(yearStart),
        to: formatDateISO(yearEnd),
        limit: 5,
      }),
    ])
    events.value = eventsRes.items
    weeklySummaries.value = weeklyRes.items
    summary.value = monthlyRes.items[0] ?? null
    yearlySummaries.value = yearlyRes.items

    if (summary.value?.highlight_event_id) {
      selectedEventId.value = summary.value.highlight_event_id
    }
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось загрузить данные'
  } finally {
    loading.value = false
  }
}

watch(monthOffset, fetchData)
onMounted(fetchData)
</script>

<template>
  <div class="month-view">
    <h2 class="view-title">Месяц</h2>

    <PeriodNav
      :label="monthLabel"
      :can-go-forward="monthOffset < 0"
      @prev="monthOffset--"
      @next="monthOffset++"
    />

    <ErrorBanner v-if="error || editError" :message="error || editError || ''" @dismiss="error = null; editError && (editError = null)" />

    <LoadingSkeleton v-if="loading" :lines="4" />

    <template v-else>
      <!-- Stats bar -->
      <div v-if="events.length" class="stats-bar">
        <span class="stats-bar__total">Всего: <strong>{{ events.length }}</strong></span>
        <div class="stats-bar__breakdown">
          <span
            v-for="(count, idx) in importanceCounts"
            :key="idx"
            v-show="count > 0"
            class="stats-bar__badge"
          >
            <StarPicker :model-value="idx + 1" readonly size="sm" />
            <span>{{ count }}</span>
          </span>
        </div>
      </div>

      <div v-if="groupedEvents.length" class="day-groups">
        <div class="action-area">
          <template v-if="isSelecting">
            <p class="action-hint">Выберите главное событие месяца</p>
          </template>
          <template v-else-if="hasSavedHighlight">
            <p class="action-hint action-hint--saved">Главное событие выбрано</p>
          </template>
          <template v-else>
            <p class="action-hint">Выберите главное событие месяца</p>
          </template>
        </div>

        <div v-for="group in groupedEvents" :key="group.date" class="day-group">
          <h4 class="day-label">{{ group.dateLabel }}</h4>
          <div v-for="evt in group.events" :key="evt.id">
            <!-- Edit mode -->
            <div v-if="editingId === evt.id && !isSelecting" class="event-form event-form--inline">
              <div class="form-field">
                <textarea v-model="editText" maxlength="500" rows="2" class="form-textarea"></textarea>
                <span class="char-count" :class="{ 'char-count--warn': editTextCharCount > 450 }">
                  {{ editTextCharCount }}/500
                </span>
              </div>
              <div class="form-field">
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
                :class="{
                  'event-card--selected': isSelecting && selectedEventId === evt.id,
                  'event-card--highlight': hasSavedHighlight && summary?.highlight_event_id === evt.id && !isSelecting,
                  'event-card--selectable': isSelecting,
                }"
                :event="evt"
                :editable="!isSelecting"
                :locked="getEventLock(evt).locked"
                :lock-reason="getEventLock(evt).reason"
                :show-importance="settingsStore.settings?.importance_enabled"
                @click="isSelecting && selectEvent(evt.id)"
                @edit="startEdit"
                @delete="deletingId = $event.id"
              />

              <!-- Delete confirmation -->
              <Transition name="form">
                <div v-if="deletingId === evt.id && !isSelecting" class="delete-confirm">
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

        <div class="action-buttons">
          <template v-if="isSelecting">
            <button class="btn btn--secondary" @click="cancelSelection">
              Отмена
            </button>
            <button
              class="btn btn--primary"
              :disabled="!selectedEventId || !hasUnsavedChanges || saving"
              @click="saveHighlight"
            >
              {{ saving ? 'Сохраняем...' : 'Сохранить' }}
            </button>
          </template>
          <template v-else-if="hasSavedHighlight">
            <button
              class="btn btn--secondary"
              :disabled="summaryLock.locked"
              :title="summaryLock.locked ? summaryLock.reason : ''"
              @click="enterSelectionMode"
            >
              <span v-if="summaryLock.locked" class="btn__lock">&#x1F512;</span>
              Редактировать
            </button>
          </template>
          <template v-else>
            <button
              class="btn btn--primary"
              :disabled="summaryLock.locked"
              :title="summaryLock.locked ? summaryLock.reason : ''"
              @click="enterSelectionMode"
            >
              <span v-if="summaryLock.locked" class="btn__lock">&#x1F512;</span>
              Выбрать главное событие
            </button>
          </template>
        </div>
      </div>

      <EmptyState v-else message="Нет событий за этот месяц" icon="month" />
    </template>
  </div>
</template>

<style scoped>
.month-view {
  max-width: 600px;
  margin: 0 auto;
}

.view-title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  text-align: center;
  text-transform: capitalize;
}

.stats-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background: var(--tg-secondary-bg-color);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.04));
  border-radius: 12px;
  font-size: 13px;
  margin: 12px 0;
}

.stats-bar__breakdown {
  display: flex;
  gap: 10px;
}

.stats-bar__badge {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
}

.action-area {
  margin: 2px 0 6px;
  text-align: center;
}

.action-hint {
  margin: 0;
  font-size: 13px;
  color: var(--tg-hint-color);
}

.action-hint--saved {
  color: var(--tg-button-color, #3390ec);
  font-weight: 500;
}

.day-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.day-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.day-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--tg-hint-color);
  margin: 0;
  text-transform: capitalize;
  letter-spacing: 0.02em;
}

:deep(.event-card.event-card--selectable) {
  cursor: pointer;
  transition: all 200ms ease;
}

:deep(.event-card.event-card--selectable:active) {
  transform: scale(0.98);
}

:deep(.event-card.event-card--selected) {
  border-color: var(--tg-button-color, #3390ec);
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 6%, var(--tg-secondary-bg-color));
}

:deep(.event-card.event-card--highlight) {
  background: color-mix(in srgb, var(--tg-button-color, #3390ec) 8%, var(--tg-secondary-bg-color));
  border-color: color-mix(in srgb, var(--tg-button-color, #3390ec) 30%, transparent);
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

.form-textarea {
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

.form-textarea:focus {
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
  display: inline-flex;
  align-items: center;
  gap: 6px;
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

.btn--primary:disabled:active {
  transform: none;
}

.btn--secondary {
  background: transparent;
  color: var(--tg-text-color);
  border: 1px solid var(--dt-card-border, rgba(0,0,0,0.12));
}

.btn--secondary:disabled {
  opacity: 0.45;
  cursor: default;
}

.btn--secondary:disabled:active {
  transform: none;
}

.btn--danger {
  background: var(--dt-error-text, #e53935);
  color: #fff;
}

.btn__lock {
  font-size: 14px;
  line-height: 1;
}

.action-buttons {
  display: flex;
  gap: 8px;
  justify-content: center;
  margin-top: 4px;
  padding-bottom: 16px;
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
</style>
