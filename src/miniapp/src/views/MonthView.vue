<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue'
import type { EventItem, Summary } from '../types'
import { getEvents } from '../api/events'
import { getSummaries } from '../api/summaries'
import { useEventEditing } from '../composables/useEventEditing'
import { isEventLocked } from '../composables/useLockCheck'
import EventCard from '../components/EventCard.vue'
import StarPicker from '../components/StarPicker.vue'
import PeriodNav from '../components/PeriodNav.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'

const events = ref<EventItem[]>([])
const weeklySummaries = ref<Summary[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

const monthOffset = ref(0)

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

function getEventLock(evt: EventItem) {
  return isEventLocked(evt.local_date, weeklySummaries.value)
}

function formatDateISO(d: Date): string {
  return d.toISOString().slice(0, 10)
}

async function fetchData() {
  loading.value = true
  error.value = null
  try {
    const { startStr, endStr } = monthRange.value
    const [eventsRes, weeklyRes] = await Promise.all([
      getEvents({ from: startStr, to: endStr, limit: 100 }),
      getSummaries('weekly', { from: startStr, to: endStr, limit: 20 }),
    ])
    events.value = eventsRes.items
    weeklySummaries.value = weeklyRes.items
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
        <div v-for="group in groupedEvents" :key="group.date" class="day-group">
          <h4 class="day-label">{{ group.dateLabel }}</h4>
          <div v-for="evt in group.events" :key="evt.id">
            <!-- Edit mode -->
            <div v-if="editingId === evt.id" class="event-form event-form--inline">
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
                :event="evt"
                :editable="true"
                :locked="getEventLock(evt).locked"
                :lock-reason="getEventLock(evt).reason"
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
