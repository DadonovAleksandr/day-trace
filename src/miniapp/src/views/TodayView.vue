<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import type { EventItem } from '../types'
import { getEvents, createEvent, updateEvent, deleteEvent } from '../api/events'

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
  return (now.getTime() - created.getTime()) < 168 * 3600 * 1000 // 7 days
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

function starsDisplay(n: number): string {
  return '★'.repeat(n) + '☆'.repeat(5 - n)
}

onMounted(fetchEvents)
</script>

<template>
  <div class="today-view">
    <div class="header">
      <h2>📝 Сегодня</h2>
      <p class="date">{{ formatDate() }}</p>
    </div>

    <!-- Error banner -->
    <div v-if="error" class="error-banner" @click="error = null">
      ❌ {{ error }}
    </div>

    <!-- Add event button -->
    <button v-if="!showForm" class="add-btn" @click="showForm = true">
      ＋ Добавить событие
    </button>

    <!-- Create event form -->
    <div v-if="showForm" class="event-form">
      <div class="form-field">
        <textarea
          v-model="newText"
          placeholder="Что произошло?"
          maxlength="500"
          rows="3"
        ></textarea>
        <span class="char-count" :class="{ warn: textCharCount > 450 }">
          {{ textCharCount }}/500
        </span>
      </div>

      <div class="form-field">
        <label>Важность:</label>
        <div class="stars-picker">
          <button
            v-for="n in 5"
            :key="n"
            :class="['star-btn', { active: n <= newImportance }]"
            @click="newImportance = n"
          >
            {{ n <= newImportance ? '★' : '☆' }}
          </button>
        </div>
      </div>

      <div class="form-field">
        <label>Дата (необязательно):</label>
        <input
          type="date"
          v-model="newLocalDate"
          :min="getMinDate()"
          :max="getTodayStr()"
        />
      </div>

      <div class="form-actions">
        <button class="btn-secondary" @click="showForm = false">Отмена</button>
        <button
          class="btn-primary"
          :disabled="!newText.trim() || newText.length > 500 || submitting"
          @click="handleCreate"
        >
          {{ submitting ? '...' : 'Сохранить' }}
        </button>
      </div>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="loading">Загрузка...</div>

    <!-- Empty state -->
    <div v-else-if="!events.length" class="empty">
      <p>Событий пока нет. Добавьте первое! 🎉</p>
    </div>

    <!-- Events list -->
    <div v-else class="events-list">
      <div
        v-for="evt in sortedEvents"
        :key="evt.id"
        class="event-card"
      >
        <!-- Edit mode -->
        <template v-if="editingId === evt.id">
          <div class="event-form inline-edit">
            <textarea v-model="editText" maxlength="500" rows="2"></textarea>
            <span class="char-count" :class="{ warn: editTextCharCount > 450 }">
              {{ editTextCharCount }}/500
            </span>
            <div class="stars-picker">
              <button
                v-for="n in 5"
                :key="n"
                :class="['star-btn', { active: n <= editImportance }]"
                @click="editImportance = n"
              >
                {{ n <= editImportance ? '★' : '☆' }}
              </button>
            </div>
            <div class="form-actions">
              <button class="btn-secondary" @click="cancelEdit">Отмена</button>
              <button
                class="btn-primary"
                :disabled="!editText.trim() || editText.length > 500 || submitting"
                @click="handleEdit(evt.id)"
              >
                {{ submitting ? '...' : 'Сохранить' }}
              </button>
            </div>
          </div>
        </template>

        <!-- Display mode -->
        <template v-else>
          <div class="event-content">
            <div class="event-text">{{ evt.text }}</div>
            <div class="event-meta">
              <span class="importance">{{ starsDisplay(evt.importance) }}</span>
              <span class="event-date">{{ evt.local_date }}</span>
            </div>
          </div>

          <!-- Actions (only within 7-day edit window) -->
          <div v-if="isEditable(evt)" class="event-actions">
            <button class="action-btn" @click="startEdit(evt)" title="Редактировать">✏️</button>
            <button class="action-btn" @click="deletingId = evt.id" title="Удалить">🗑️</button>
          </div>
        </template>

        <!-- Delete confirmation -->
        <div v-if="deletingId === evt.id" class="delete-confirm">
          <p>Удалить событие?</p>
          <div class="form-actions">
            <button class="btn-secondary" @click="deletingId = null">Нет</button>
            <button class="btn-danger" :disabled="submitting" @click="handleDelete(evt.id)">
              {{ submitting ? '...' : 'Да, удалить' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.today-view {
  max-width: 600px;
  margin: 0 auto;
}

.header h2 {
  margin: 0;
}

.date {
  color: var(--tg-hint-color);
  font-size: 13px;
  margin-top: 4px;
  text-transform: capitalize;
}

.error-banner {
  background: #fee;
  border: 1px solid #fcc;
  border-radius: 8px;
  padding: 8px 12px;
  margin: 12px 0;
  font-size: 13px;
  cursor: pointer;
}

.add-btn {
  width: 100%;
  padding: 12px;
  margin: 16px 0;
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
  border: none;
  border-radius: 12px;
  font-size: 15px;
  cursor: pointer;
}

.event-form {
  background: var(--tg-secondary-bg-color);
  border-radius: 12px;
  padding: 12px;
  margin: 12px 0;
}

.inline-edit {
  margin: 0;
}

.form-field {
  margin-bottom: 12px;
  position: relative;
}

.form-field label {
  display: block;
  font-size: 13px;
  color: var(--tg-hint-color);
  margin-bottom: 4px;
}

textarea,
input[type="date"] {
  width: 100%;
  padding: 8px;
  border: 1px solid var(--tg-hint-color);
  border-radius: 8px;
  font-size: 14px;
  background: var(--tg-bg-color);
  color: var(--tg-text-color);
  resize: none;
}

.char-count {
  position: absolute;
  right: 8px;
  bottom: 4px;
  font-size: 11px;
  color: var(--tg-hint-color);
}

.char-count.warn {
  color: #e53935;
}

.stars-picker {
  display: flex;
  gap: 4px;
}

.star-btn {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: var(--tg-hint-color);
  padding: 2px 4px;
}

.star-btn.active {
  color: #ffc107;
}

.form-actions {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
  margin-top: 8px;
}

.btn-primary,
.btn-secondary,
.btn-danger {
  padding: 8px 16px;
  border: none;
  border-radius: 8px;
  font-size: 14px;
  cursor: pointer;
}

.btn-primary {
  background: var(--tg-button-color);
  color: var(--tg-button-text-color);
}

.btn-primary:disabled {
  opacity: 0.5;
}

.btn-secondary {
  background: var(--tg-secondary-bg-color);
  color: var(--tg-text-color);
  border: 1px solid var(--tg-hint-color);
}

.btn-danger {
  background: #e53935;
  color: #fff;
}

.loading,
.empty {
  text-align: center;
  padding: 32px 0;
  color: var(--tg-hint-color);
}

.events-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.event-card {
  background: var(--tg-secondary-bg-color);
  border-radius: 12px;
  padding: 12px;
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: 8px;
}

.event-content {
  flex: 1;
  min-width: 0;
}

.event-text {
  word-break: break-word;
  margin-bottom: 4px;
}

.event-meta {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  color: var(--tg-hint-color);
}

.importance {
  color: #ffc107;
  letter-spacing: 1px;
}

.event-actions {
  display: flex;
  gap: 4px;
}

.action-btn {
  background: none;
  border: none;
  font-size: 16px;
  cursor: pointer;
  padding: 4px;
}

.delete-confirm {
  width: 100%;
  background: #fff3e0;
  border-radius: 8px;
  padding: 8px;
  margin-top: 4px;
}

.delete-confirm p {
  margin-bottom: 8px;
  font-size: 13px;
}
</style>
