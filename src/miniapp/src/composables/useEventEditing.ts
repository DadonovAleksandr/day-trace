import { ref } from 'vue'
import type { EventItem } from '../types'
import { updateEvent, deleteEvent } from '../api/events'

export function useEventEditing(onSuccess: () => Promise<void>) {
  const editingId = ref<string | null>(null)
  const editText = ref('')
  const editImportance = ref(3)
  const deletingId = ref<string | null>(null)
  const submitting = ref(false)
  const editError = ref<string | null>(null)

  function startEdit(evt: EventItem) {
    editingId.value = evt.id
    editText.value = evt.text
    editImportance.value = evt.importance
  }

  function cancelEdit() {
    editingId.value = null
    editError.value = null
  }

  async function handleEdit(id: string) {
    if (!editText.value.trim() || editText.value.length > 500) return
    submitting.value = true
    editError.value = null
    try {
      await updateEvent(id, {
        text: editText.value.trim(),
        importance: editImportance.value,
      })
      editingId.value = null
      await onSuccess()
    } catch (err: any) {
      editError.value = err.response?.data?.message || 'Не удалось обновить событие'
    } finally {
      submitting.value = false
    }
  }

  async function handleDelete(id: string) {
    submitting.value = true
    editError.value = null
    try {
      await deleteEvent(id)
      deletingId.value = null
      await onSuccess()
    } catch (err: any) {
      editError.value = err.response?.data?.message || 'Не удалось удалить событие'
    } finally {
      submitting.value = false
    }
  }

  return {
    editingId,
    editText,
    editImportance,
    deletingId,
    submitting,
    editError,
    startEdit,
    cancelEdit,
    handleEdit,
    handleDelete,
  }
}
