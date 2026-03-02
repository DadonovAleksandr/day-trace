# Code Review: монетизация (подписки / Telegram Stars)

## Контекст
- Объект ревью: незакоммиченные изменения в `DayTrace.Api`, `DayTrace.Domain`, `DayTrace.Infrastructure`, `miniapp`, `admin-ui`, `tests`.
- Фокус: риски регрессий, уязвимые места в бизнес-логике и API-контрактах, качество покрытия тестами.

## Замечания (по приоритету)

### 1. Критично: неатомарная активация платежа может привести к потере оплаченного доступа
- Файл: `src/DayTrace.Domain/Services/SubscriptionService.cs` (метод `ActivateAsync`).
- Проблема: запись о платеже создается раньше продления подписки и отдельным `SaveChangesAsync`.
- Риск: если после сохранения платежа упадет обновление подписки, повторная обработка будет заблокирована дедупликацией по `chargeId`, а доступ не активируется.
- Что улучшить:
  - выполнять запись платежа и обновление/создание подписки в одной транзакции;
  - сделать обработку идемпотентной так, чтобы при повторе можно было безопасно «дозавершить» активацию.

### 2. Критично: неверный приоритет статусов подписки
- Файл: `src/DayTrace.Domain/Services/SubscriptionService.cs` (метод `GetStatusAsync`).
- Проблема: при `TrialStartedAt == null` статус сразу возвращается как `NotStarted` до проверки `SubscriptionExpiresAt`.
- Риск: пользователь с оплаченной подпиской, но без trial-полей, может получить статус `not_started`.
- Что улучшить:
  - переразложить приоритет статусов: `Exempt -> Active -> Trial -> GracePeriod -> Expired -> NotStarted`.

### 3. Высокий: рассинхрон формата статуса в admin detail
- Файл: `src/DayTrace.Api/Controllers/AdminSubscriptionsController.cs`.
- Проблема: в detail статус отдается через `statusResult.Status.ToString().ToLowerInvariant()`, что дает `graceperiod`/`notstarted`, а не `grace_period`/`not_started`.
- Риск: фронтенд-контракт нарушается, отображение/фильтрация статусов в админке становятся нестабильными.
- Что улучшить:
  - использовать единый маппер `ToStatusString(...)` и для list, и для detail.

### 4. Высокий: фильтрация статусов в админке неполная и частично неверная
- Файл: `src/DayTrace.Infrastructure/Repositories/SubscriptionRepository.cs` (метод `GetAllAsync`).
- Проблема:
  - не поддержаны фильтры `grace_period` и `not_started`;
  - `expired` логика не учитывает grace window (7 дней) и может включать пользователей в grace.
- Риск: админ-аналитика и операционная работа по подпискам дают неверную картину.
- Что улучшить:
  - привести SQL/EF фильтры в соответствие бизнес-логике `GetStatusAsync`;
  - покрыть фильтры интеграционными тестами по каждому статусу.

### 5. Средний: `telegram_id` в detail может быть `null` даже при наличии пользователя
- Файлы:
  - `src/DayTrace.Infrastructure/Repositories/SubscriptionRepository.cs` (`GetByUserIdAsync`);
  - `src/DayTrace.Api/Controllers/AdminSubscriptionsController.cs` (`GetDetail`).
- Проблема: `GetByUserIdAsync` не подгружает `User`, но контроллер читает `sub.User?.TelegramUserId`.
- Риск: неконсистентные данные в админке.
- Что улучшить:
  - добавить `Include(s => s.User)` для detail-сценария или выделить отдельный projection-метод.

### 6. Средний: тест checkout слишком мягкий и может маскировать реальные проблемы
- Файл: `tests/DayTrace.Tests/Integration/SubscriptionTests.cs` (тест `Checkout_MonthlyPlan_AcceptsRequest`).
- Проблема: тест допускает `500` и проверяет только что не `400/401`.
- Риск: критические ошибки интеграции с Telegram/checkout не будут ловиться в CI.
- Что улучшить:
  - стабилизировать мок `ITelegramBotClient` и проверять ожидаемый успешный код (`200`) и валидный `invoice_link`.

## Дополнительные предложения
- Вынести тарифы (`monthly=100`, `annual=960`) и длительности (`30/365`) в централизованную конфигурацию/константы домена, чтобы исключить расхождения между API, bot handler и UI.
- Добавить базовую валидацию payload платежа (минимум: версия схемы payload и проверка связки user/plan).
- Проверить политику middleware-исключений (`SubscriptionCheckMiddleware`/`ClientOperationIdMiddleware`) на предмет случайного доступа к платным эндпоинтам через новые роуты в будущем.

## Статус тестового прогона по ревью
- `SubscriptionTests`: пройдены (`19 passed`).
- `SubscriptionServiceTests` и `BotUpdateHandlerTests`: не стартовали из-за ошибки restore/build (`*.nuget.g.props/targets already exists`).

## Повторная проверка (2026-03-02)

### Статус устранения предыдущих замечаний
- `#1 (атомарность оплаты)`: частично устранено. Транзакция добавлена в `BotUpdateHandler`, но не в `SubscriptionService`.
- `#2 (приоритет статусов)`: устранено.
- `#3 (формат статуса в admin detail)`: устранено.
- `#4 (фильтры статусов админки)`: устранено частично.
- `#5 (telegram_id в detail)`: устранено.
- `#6 (слабый checkout тест)`: устранено.

### Оставшиеся/новые замечания

### 7. Высокий: фильтр `not_started` все еще не соответствует бизнес-логике статусов
- Файл: `src/DayTrace.Infrastructure/Repositories/SubscriptionRepository.cs` (метод `GetAllAsync`).
- Проблема: фильтр `not_started` включает пользователей с `subscription_expires_at <= now`, что может захватывать кейсы фактически `grace_period/expired` для пользователей без trial.
- Риск: неверная сегментация в админке, искажение аналитики и ручных действий операторов.
- Что улучшить:
  - синхронизировать условие `not_started` с `SubscriptionService.GetStatusAsync`;
  - исключить из `not_started` записи, где исторически была paid-подписка и статус уже должен быть `grace_period/expired`.

### 8. Средний: атомарность активации не инкапсулирована в доменном сервисе
- Файлы:
  - `src/DayTrace.Bot/Handlers/BotUpdateHandler.cs` (`BeginTransactionAsync` вокруг `ActivateAsync`);
  - `src/DayTrace.Domain/Services/SubscriptionService.cs` (`ActivateAsync`).
- Проблема: транзакционная гарантия работает только в текущем месте вызова.
- Риск: при новом месте вызова `ActivateAsync` без явной транзакции снова возможна частичная запись (платеж есть, подписка не продлена).
- Что улучшить:
  - перенести транзакционную границу в `SubscriptionService` (или в отдельный application service), чтобы вызов был безопасным по умолчанию.

### 9. Средний: подтверждение исправлений ограничено инфраструктурной ошибкой тестового окружения
- Файл/контекст: запуск `dotnet test` (ошибка `*.nuget.g.props already exists`).
- Проблема: полный регрессионный прогон не завершается стабильно.
- Риск: нельзя формально подтвердить, что исправления полностью безопасны в CI-подобном сценарии.
- Что улучшить:
  - стабилизировать test build pipeline/кэш `obj` и повторить полный целевой прогон тестов.

## Дополнительные предложения по улучшению после повторной проверки
- Добавить интеграционные тесты именно на фильтры `/admin/subscriptions?status=...` для `not_started`, `grace_period`, `expired`.
- Добавить unit/integration тест на идемпотентность и транзакционность `ActivateAsync` (включая сценарий сбоя между записью платежа и обновлением подписки).
