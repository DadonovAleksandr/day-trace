import type { Summary } from '../types'

/**
 * Проверяет, заблокировано ли событие (по дате) наличием weekly summary со status='generated'.
 * По дате события вычисляет неделю (ISO: понедельник-воскресенье), ищет matching weekly summary.
 */
export function isEventLocked(
  eventDate: string,
  weeklySummaries: Summary[]
): { locked: boolean; reason: string } {
  const date = new Date(eventDate + 'T00:00:00')

  for (const s of weeklySummaries) {
    if (s.status !== 'generated') continue
    const start = new Date(s.period_start + 'T00:00:00')
    const end = new Date(s.period_end + 'T23:59:59')
    if (date >= start && date <= end) {
      return { locked: true, reason: 'Итог недели уже сформирован' }
    }
  }

  return { locked: false, reason: '' }
}

/**
 * Проверяет, заблокирована ли перегенерация сводки наличием сводки следующего уровня.
 * weekly -> проверяет monthly, monthly -> проверяет yearly.
 */
export function isSummaryLocked(
  periodType: string,
  periodStart: string,
  periodEnd: string,
  parentSummaries: Summary[]
): { locked: boolean; reason: string } {
  for (const s of parentSummaries) {
    if (s.status !== 'generated') continue
    const pStart = new Date(s.period_start + 'T00:00:00')
    const pEnd = new Date(s.period_end + 'T23:59:59')
    const start = new Date(periodStart + 'T00:00:00')
    const end = new Date(periodEnd + 'T00:00:00')

    // Проверяем, что текущий период пересекается с родительской сводкой
    if (start <= pEnd && end >= pStart) {
      if (periodType === 'weekly') {
        return { locked: true, reason: 'Итог месяца уже сформирован' }
      } else if (periodType === 'monthly') {
        return { locked: true, reason: 'Итог года уже сформирован' }
      }
    }
  }

  return { locked: false, reason: '' }
}
