# Production Broadcast Queue Prompt

Нужно реализовать production-версию массовой рассылки в админке DayTrace через очередь, а не синхронную отправку в HTTP.

## Контекст (что уже есть)

- Сейчас есть MVP endpoint `POST /admin/messaging/broadcast`, который шлет сообщения синхронно.
- Есть `delivery_attempts` и `DeliveryRetryService`, который умеет ретраи.
- Есть `admin_broadcast` в delivery attempts (MVP), но для production нужна отдельная сущность кампании.
- В админке уже есть UI формы рассылки в `src/admin-ui/src/views/OperationsView.vue`.

## Что нужно сделать (цель)

- Сделать рассылку production-подходом:
  - HTTP endpoint только создает кампанию + ставит задания в очередь (`delivery_attempts`)
  - фактическая отправка идет фоном через существующий retry/worker механизм
  - добавить отдельную сущность кампании (таблица + репозиторий + API для просмотра статуса)
- Сохранить текущий MVP reply (`/admin/feedback/{id}/reply`) как есть, если это не мешает.

## Обязательные требования (backend)

- Добавить сущность кампании рассылки (например `AdminBroadcastCampaign` / `BroadcastCampaign`) с полями минимум:
  - `Id`
  - `CreatedByAdminId`
  - `Audience` (`active` / `reminders`)
  - `Text`
  - `Status` (`queued`, `completed`, `partial_failed`, `failed` — можно расширить)
  - `CreatedAt`
  - `QueuedAt`
  - `CompletedAt` (nullable)
- Добавить EF mapping в `DayTraceDbContext` + миграцию.
- Добавить репозиторий/интерфейс для кампаний.
- Изменить `POST /admin/messaging/broadcast`:
  - не отправлять в Telegram в этом HTTP запросе
  - валидировать payload (`text`, `audience`)
  - создать кампанию
  - enqueue по пользователям в `delivery_attempts` с `delivery_type = "admin_broadcast"` и `reference_id = campaignId`
  - вернуть быстрый ответ с `campaign_id`, `status`, `queued_count`, `audience`
- Добавить admin endpoints для чтения статуса кампаний (минимум один из вариантов):
  - `GET /admin/messaging/broadcasts` (list)
  - `GET /admin/messaging/broadcasts/{id}` (detail)
- Прогресс кампании можно считать агрегированием `delivery_attempts` (sent/terminal_failed/pending/failed) по `reference_id` — не обязательно хранить счетчики в таблице кампании, если это упрощает реализацию.
- Обновить `DeliveryRetryService`, чтобы он умел отправлять `admin_broadcast`:
  - по `reference_id` загружать кампанию и брать текст кампании
  - не падать на отсутствии кампании: помечать attempt как `terminal_failed`
  - сохранить текущую логику ретраев
- Не отправлять `admin_broadcast` синхронно из контроллера.
- Писать аудит для создания кампании/просмотра (через уже существующий `IAdminAuditService`).

## Обязательные требования (frontend)

- Обновить `OperationsView.vue`:
  - после отправки формы рассылки ожидать ответ очереди (`campaign_id`, `queued_count`, `status`)
  - показать, что рассылка поставлена в очередь (а не отправлена сразу)
  - добавить просмотр статуса кампаний (минимум список последних кампаний или карточка текущей)
- Обновить `src/admin-ui/src/api/admin.ts` и `src/admin-ui/src/types/index.ts` под новый контракт.
- Не ломать текущий мониторинг `delivery_attempts`.

## Технические ограничения

- Используй sub-агентов.
- Не трогай unrelated изменения в репозитории (есть грязное дерево).
- Не переписывай старые миграции/автоген файлы без необходимости (только новая миграция для новой таблицы).
- Не ломай существующие admin auth/cookie изменения и reply MVP.
- Если меняешь контракт API, синхронизируй backend и frontend.

## Что вернуть в ответе

- Краткое summary изменений
- Список измененных файлов
- Что именно поменялось в API (request/response)
- Какие проверки прошли / что не удалось запустить
- Отдельно перечислить риски/долги (если что-то оставил упрощенным)
