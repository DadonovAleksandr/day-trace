<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import type { EventItem } from '../types'
import { getEvents, createEvent, updateEvent, deleteEvent } from '../api/events'
import EventCard from '../components/EventCard.vue'
import StarPicker from '../components/StarPicker.vue'
import ErrorBanner from '../components/ErrorBanner.vue'
import EmptyState from '../components/EmptyState.vue'
import LoadingSkeleton from '../components/LoadingSkeleton.vue'
import AppIcon from '../components/AppIcon.vue'

const events = ref<EventItem[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

// New event form
const showForm = ref(false)
const newText = ref('')
const newImportance = ref(3)
const newLocalDate = ref('')
const submitting = ref(false)

// Edit state
const editingId = ref<string | null>(null)
const editText = ref('')
const editImportance = ref(3)

// Delete confirmation
const deletingId = ref<string | null>(null)

const sortedEvents = computed(() =>
  [...events.value].sort((a, b) => b.importance - a.importance)
)

const textCharCount = computed(() => newText.value.length)
const editTextCharCount = computed(() => editText.value.length)

function isEditable(evt: EventItem): boolean {
  const created = new Date(evt.created_at)
  const now = new Date()
  return (now.getTime() - created.getTime()) < 168 * 3600 * 1000
}

async function fetchEvents() {
  loading.value = true
  error.value = null
  try {
    const result = await getEvents()
    events.value = result.items
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
      importance: newImportance.value,
      local_date: newLocalDate.value || undefined,
    })
    newText.value = ''
    newImportance.value = 3
    newLocalDate.value = ''
    showForm.value = false
    await fetchEvents()
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось создать событие'
  } finally {
    submitting.value = false
  }
}

function startEdit(evt: EventItem) {
  editingId.value = evt.id
  editText.value = evt.text
  editImportance.value = evt.importance
}

function cancelEdit() {
  editingId.value = null
}

async function handleEdit(id: string) {
  if (!editText.value.trim() || editText.value.length > 500) return
  submitting.value = true
  try {
    await updateEvent(id, {
      text: editText.value.trim(),
      importance: editImportance.value,
    })
    editingId.value = null
    await fetchEvents()
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось обновить событие'
  } finally {
    submitting.value = false
  }
}

async function handleDelete(id: string) {
  submitting.value = true
  try {
    await deleteEvent(id)
    deletingId.value = null
    await fetchEvents()
  } catch (err: any) {
    error.value = err.response?.data?.message || 'Не удалось удалить событие'
  } finally {
    submitting.value = false
  }
}

function formatDate(): string {
  const today = new Date()
  return today.toLocaleDateString('ru-RU', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })
}

function getMinDate(): string {
  const d = new Date()
  d.setDate(d.getDate() - 30)
  return d.toISOString().slice(0, 10)
}

function getTodayStr(): string {
  return new Date().toISOString().slice(0, 10)
}

onMounted(fetchEvents)
</script>

<template>
  <div class="today-view">
    <div class="header">
      <h2 class="header__title">Сегодня</h2>
      <p class="header__date">{{ formatDate() }}</p>
    </div>

    <ErrorBanner v-if="error" :message="error" @dismiss="error = null" />

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

        <div class="form-field">
          <label class="form-label">Важность</label>
          <StarPicker v-model="newImportance" />
        </div>

        <div class="form-field">
          <label class="form-label">Дата (необязательно)</label>
          <input
            type="date"
            v-model="newLocalDate"
            :min="getMinDate()"
            :max="getTodayStr()"
            class="form-input"
          />
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
          <div class="form-field">
            <StarPicker v-model="editImportance" />
          </div>
          <div class="form-actions">
            <button class="btn btn--secondary" @click="cancelEdit">Отмена</button>
            <button
              class="btn btn--primary"
              :disabled="!editText.trim() || editText.length > 500 || submitting"
              @click="handleEdit(evt.id)"
            >
              {{ submitting ? 'Сохраняем...' : 'Сохранить' }}
            </button>
          </div>
        </div>

        <!-- Display mode -->
        <template v-else>
          <EventCard
            :event="evt"
            :editable="isEditable(evt)"
            @edit="startEdit"
            @delete="deletingId = $event.id"
          />

          <!-- Delete confirmation -->
          <Transition name="form">
            <div v-if="deletingId === evt.id" class="delete-confirm">
              <p>Удалить событие?</p>
              <div class="form-actions">
                <button class="btn btn--secondary" @click="deletingId = null">Нет</button>
                <button class="btn btn--danger" :disabled="submitting" @click="handleDelete(evt.id)">
                  {{ submitting ? '...' : 'Да, удалить' }}
                </button>
              </div>
            </div>
          </Transition>
        </template>
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
}

.header__date {
  color: var(--tg-hint-color);
  font-size: 13px;
  margin-top: 2px;
  text-transform: capitalize;
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
</style>
