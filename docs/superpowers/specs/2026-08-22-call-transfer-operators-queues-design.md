# Перевод звонка на операторов и очереди — дизайн (фаза 1: слепой перевод)

Дата: 2026-08-22
Статус: утверждён; реализуется по плану `docs/superpowers/plans/2026-08-22-call-transfer-operators-queues.md`
Репозитории: `vv-phone-widget` (виджет, .NET/Avalonia), `crm_mono` (бэк, NestJS/Nx)

> **Важно про ветку.** Раздел «Что уже есть» описывает `feat/freeswitch-all`, на
> которой стояло рабочее дерево при написании спека. Фича шипится в **`main`**,
> где драйверной абстракции `TelephonyCallControlDriver`, `channel-locator.ts` и
> файла `dialplan-context.util.ts` **нет**, а `POST /calls/transfer` — это
> ARI-овый `CallTransferService` с телом `{channelId, target}`. Решения
> раздела 3 в силе; конкретные точки правок для main перечислены в плане, там же
> — таблица расхождений и порядок переноса на `feat/freeswitch-all`.

## 1. Задача

В виджете перевод звонка сейчас — текстовое поле, куда оператор вводит номер руками
(`TransferPanel`, `OrbitalSIP/Views/ActiveCallView.axaml:116`), плюс отдельная кнопка
«перевести владельцу лида». Нужен выбор цели из списка, как в веб-софтфоне: **по операторам
и по очередям**, с проверкой прав (RBAC) и изоляцией по организациям.

Фаза 1 покрывает слепой (blind) перевод. Консультативный (attended) вынесен в фазу 2 —
отдельный спек, отдельный план.

## 2. Что уже есть

### Виджет

- `SipService.BlindTransferAsync(destination)` — SIP REFER, работает
  (`OrbitalSIP/Services/SipService.cs:770`).
- `OnTransferRequested` несёт голую строку; `MainWindow` безусловно вызывает
  `BlindTransferAsync` (`OrbitalSIP/MainWindow.axaml.cs:763`).
- Резолв `callId` (первичный Asterisk linkedid) через
  `GET /api/cdr/channel-uniqueid?callerNumber=…` — реализован **дважды**:
  `Services/ScriptService.cs:84` и `Services/FlowsService.cs:310`.
- Один `_activeCall` и один `VoIPMediaSession` (`Services/SipService.cs:29,31`).
- i18n — плоский словарь `Assets/i18n/<lang>.json`, четыре локали: `ru`, `kk`, `tg`, `uz`.
- Чистые презентеры отделены от вида и покрыты тестами (`LeadCallPanelPresenter` +
  `OrbitalSIP.Tests/LeadCallPanelPresenterTests.cs`) — образец для новой логики.

### Бэк

- `POST /api/calls/transfer` — PBX-агностичный, через `TelephonyCallControlDriver.transfer()`,
  ability `calls:create`, `RequireTenantGuard`. Endpoint оператора берётся **из токена**,
  не из тела. Изоляция источника — `subscriberLeg()`: звонок, где оператор не сторона,
  даёт больше одного кандидата и отвергается.
- `GET /api/presence/board` (`presence:read`, tenant-scoped) — операторы с телефонным
  оверлеем `sipRegistered` / `onCall` / `supervisorPausedBy`.
- `GET /api/contact-center/queues` (`calls:read`, tenant-scoped) — есть, но тяжёлый:
  AMI-раундтрип, участники, дневные сводки.
- `GET /api/queues` — закрыт под `settings:manage`, оператору недоступен.
- Драйверы: Asterisk (AMI `Redirect` / `Atxfer`) и FreeSWITCH (`uuid_transfer`), выбор в
  рантайме по `TELEPHONY_BACKEND`. Конформанс-сьют
  `telephony-call-control-conformance.vitest.ts` заставляет оба драйвера отвечать
  на весь интерфейс.

### Дыры, которые закрывает этот дизайн

1. **Очередь недостижима переводом.** `transfer()` редиректит на *экстеншн в контексте*.
   `Queue()` генерится только для DID/IVR/flow-веток (`from-trunk`); ни в `from-internal`
   (Asterisk), ни в `org-<uuid>-internal` (FreeSWITCH) имени очереди как экстеншна нет.
2. **Цель перевода не изолирована по организации.** `assertSafeToken` проверяет только
   символы. `subscriberLeg` изолирует источник, цель — никто.
3. **Asterisk-драйвер игнорирует `organizationId`.** Параметр объявлен как `_organizationId`
   и не используется: экстеншн-перевод уходит в глобальный `ASTERISK_FROM_ARI_CONTEXT`,
   тогда как FreeSWITCH-драйвер берёт `contextForOrg(orgId, 'internal')`.
4. **Веб-диалог перевода не смотрит `supervisorPausedBy`.** Докблок `PresenceBoardRow`
   прямо требует смотреть оба поля («гейт перевода звонка … — тоже»), а
   `transfer-dialog.component.ts` фильтрует только по `manualStatus`. Оператор на
   принудительной паузе супервайзера предлагается как цель.
5. **Операторский перевод не пишется в аудит.** Ключ `calls.transfer` в
   `audit-log.entity` есть, супервайзерский путь пишет, операторский — нет.

## 3. Принятые решения

| Вопрос | Решение |
|---|---|
| Тип перевода | Blind в фазе 1, attended — фаза 2 |
| Кто исполняет перевод | Бэк (`POST /api/calls/transfer`); виджет — UI и вызов, плюс SIP-фолбэк при недоступном бэке (см. 4.2 C4) |
| Как очередь становится целью | Новый per-org контекст `org-<uuid>-queues` |
| RBAC | Гейт по существующим ability плюс тенант-скоуп. Новых ability не заводим |
| Сужение по департаменту (ADR-020) | Вне объёма |

## 4. Архитектура

### 4.1. Бэк

**B1. `DialplanContextKind` получает `'queues'`**
`apps/back/src/app/modules/sip-trunk/dialplan-context.util.ts:27`. Файл нарочно без
entity-импортов — держим его таким.

**B2. Контекст `org-<uuid>-queues`**
Новый чистый модуль `pjsip/services/queue-transfer.dialplan.ts` рядом с
`operator-provisioning.dialplan.ts`. Две строки на организацию:

```
_.  1  Queue    ${EXTEN},n
_.  2  Hangup   —
```

Оба приложения уже в allowlist `dialplan-validator.service.ts`. Catch-all `_.` безопасен:
контекст недостижим с эндпоинта, только явным `Redirect` от драйвера.

`ensureQueueTransferContext(orgId, manager)` — тот же паттерн, что
`ensureInternalOutboundRoute` (`pjsip/services/operator-provisioning.service.ts:290`):
`INSERT INTO extensions … ON CONFLICT ("context","exten","priority") DO NOTHING`.
Плюс миграция, прогоняющая это по всем существующим организациям.

На FreeSWITCH тот же контекст должен появиться в рендеренном конфиге — это деплой
(ADR-023 §2), не рантайм-запись. План обязан назвать это отдельным шагом.

**B3. Драйвер узнаёт тип цели**
`TelephonyCallControlDriver.transfer()` получает шестой параметр
`targetKind: 'extension' | 'queue'`.

- Asterisk: `Context = targetKind === 'queue' ? contextForOrg(orgId, 'queues')
  : ASTERISK_FROM_ARI_CONTEXT`. Побочно закрывает дыру 3.
- FreeSWITCH: тот же выбор контекста в `uuid_transfer <uuid> <target> XML <ctx>`.
- `organizationId == null` при `targetKind === 'queue'` — бросаем, а не молча
  подставляем дефолтный контекст.
- Конформанс-сьют расширяется: оба драйвера обязаны ответить на `queue`.

**B4. `TransferTargetService`** (calls-модуль; инжектит `PresenceBoardService` —
`PresenceModule` его экспортит — и репозитории `Queue`, `User`)

- `listTargets()` возвращает `{ operators, queues }`, оба tenant-scoped.
  - Операторы: `PresenceBoardService.getBoard()`, фильтр
    `sipRegistered && !onCall && manualStatus === null && supervisorPausedBy === null`,
    минус сам себя. Это закрывает дыру 4 на стороне виджета; веб-диалог остаётся как есть
    (его правка — отдельная задача).
  - Очереди: `queues` под `tenantService.applyScope`, поля `name`, `description`.
    Живую статистику не тянем — это AMI-раундтрип на каждое открытие панели.

  Форма ответа:

  ```jsonc
  {
    "operators": [{ "extension": "1042", "fullName": "…", "status": "online" }],
    "queues": [
      { "name": "sales", "description": "…", "disabledReason": null },
      { "name": "Отдел продаж", "description": "…", "disabledReason": "unsafe-name" }
    ]
  }
  ```

  `disabledReason` — `null` либо `"unsafe-name"`. Других значений в фазе 1 нет; новое
  значение обязано приходить вместе с новым i18n-ключом в виджете.
- `assertTargetAllowed(targetKind, value)` — вызывается **до** драйвера:
  - `queue` → имя обязано быть в `queues` своей организации;
  - `extension` → значение обязано быть `users.sipEndpointId` пользователя своей
    организации.

**Имена очередей.** `SAFE_TOKEN = /^[A-Za-z0-9_.@-]+$/` — ни пробелов, ни кириллицы.
`queues.name` — свободный `text`; ограничение `/^[A-Za-z0-9_-]+$/` применяется только в
`did-to-queue`, на колонку его нет. Очередь «Отдел продаж» может существовать и быть
недостижимой переводом. Такую очередь **не выкидываем из списка молча** — отдаём с
`disabledReason`, UI рисует её серой с подсказкой.

**B5. Контроллер** (`calls/controllers/calls.controller.ts`)

- `GET /api/calls/transfer-targets`, `@CheckAbility('calls:create')` — тот же гейт, что и
  у самого перевода: не можешь переводить — список не нужен.
- `POST /api/calls/transfer`: тело получает `targetKind?`, дефолт `'extension'` —
  веб-софтфон не ломается. Перед драйвером — `assertTargetAllowed`.
- Аудит `calls.transfer` для операторского пути (дыра 5).

### 4.2. Виджет

**C1. `TransferService`** (`Services/TransferService.cs`), по шаблону `FlowsService`:
`IDisposable`, свой `HttpClient` с укороченным таймаутом, Bearer из `SipSettings`,
ошибки через `HttpErrorNotifier`.

- `GetTargetsAsync(ct)` → `GET /api/calls/transfer-targets`
- `TransferAsync(kind, value, ct)` → резолв `callId`, затем `POST /api/calls/transfer`.
  Возвращает результат из трёх состояний: `Ok`, `Failed(сообщение)`,
  `CallIdUnresolved` — последнее MainWindow отличает от обычной ошибки, потому что
  именно оно включает SIP-фолбэк для оператора (см. C4).

Резолв `callId` вынести в общий хелпер: сейчас копий две, третью не плодим.

**C2. `TransferTargetsPresenter`** — чистая логика без Avalonia, рядом с
`LeadCallPanelPresenter`:

- `SelectState(targets, loading, error, forbidden)` → `Loading | Error | Forbidden | Empty | Ready`
- `Filter(targets, query, tab, ownExtension)` — поиск по имени и экстеншну без учёта
  регистра, самоисключение оператора по `ownExtension`, сортировка по имени; очереди с
  непустым `disabledReason` уходят в конец списка
- `QueueDisabledKey(reason)` → i18n-ключ, по образцу `TransferBlockedKey`
  (`Services/LeadCallPanelPresenter.cs:193`)

**C3. UI.** `TransferPanel` расширяется: два таб-тоггла «Операторы» / «Очереди», строка
поиска, `ScrollViewer` с ограниченной высотой, под ним — существующее поле ручного ввода.
Код-бихайнд плюс `FindControl`, как весь остальной вид; MVVM не заводим.
Строка оператора — имя, экстеншн, точка статуса. Строка очереди — имя и описание.

**C4. Событие меняет форму.** `OnTransferRequested` несёт дескриптор `(kind, value)`
вместо строки. Маршрутизация в `MainWindow`:

| Ситуация | Действие |
|---|---|
| оператор, бэк ответил | `POST /calls/transfer` |
| оператор, бэк недоступен / 403 / `callId` не резолвится | SIP REFER (текущее поведение) |
| очередь, бэк ответил | `POST /calls/transfer`, `targetKind: 'queue'` |
| очередь, бэк недоступен | ошибка, **без** фолбэка — REFER на имя очереди уедет в никуда |
| ручной ввод | SIP REFER |

SIP-фолбэк сохраняется намеренно: убрать его — регресс работающего поведения при
недоступном бэке.

**C5. i18n.** Новые ключи во все четыре локали: табы, поиск, пусто, ошибка загрузки,
повтор, «очередь недоступна для перевода», успех, неудача. Существующие `Transfer`,
`TransferToOperator`, `OperatorNumber` переиспользуются.

## 5. Поток данных

```
[Transfer] → GET /api/calls/transfer-targets   (Bearer, calls:create, tenant-scoped)
           → табы «Операторы» / «Очереди»
тап по цели → GET /api/cdr/channel-uniqueid?callerNumber=…   → callId
           → POST /api/calls/transfer { callId, targetExtension, targetKind, type:'blind' }
             ├─ assertTargetAllowed  (цель принадлежит моей организации)
             ├─ subscriberLeg        (я сторона этого звонка)
             └─ driver.transfer      (Redirect / uuid_transfer в нужный контекст)
           → { ok: true } → тост «переведено», PBX присылает BYE
```

## 6. Обработка ошибок

| Отказ | Поведение |
|---|---|
| `GET transfer-targets` → 401 | Обновление токена существующим `AuthRefreshHandler` |
| `GET transfer-targets` → 403 | Табы скрыты, остаётся ручной ввод; пишем в лог |
| `GET transfer-targets` → 5xx или таймаут | Состояние `Error` с кнопкой повтора |
| `POST transfer` → `{ok:false,error}` | Тост с текстом; панель открыта, звонок не тронут |
| `callId` не резолвится | Оператор → SIP-фолбэк; очередь → ошибка |
| Очередь с недиалабельным именем | Серая строка с подсказкой, тап игнорируется |

## 7. Тесты

**Виджет**

- `TransferTargetsPresenterTests` — состояния, поиск, самоисключение, disabled-очередь
- `TransferServiceTests` — мок `HttpMessageHandler` по образцу `FlowsServiceTests`:
  200 / 403 / 5xx / `{ok:false}`; `callId` резолвится **до** POST; без `callId` оператор
  уходит в SIP-фолбэк, а очередь — в ошибку

**Бэк**

- Юнит на `queueTransferRows()`
- Юнит на `assertTargetAllowed` — очередь чужой организации и чужой `sipEndpointId`
  получают отказ
- Расширение `telephony-call-control-conformance.vitest.ts` на `targetKind: 'queue'`
- Юнит на выбор контекста в обоих драйверах, включая отказ при `orgId == null` и `queue`

## 8. Вне объёма

- Консультативный (attended) перевод — фаза 2. Требует второго SIP UA и второй
  медиа-сессии в виджете (сейчас по одной, `SipService.cs:29,31`), переключения
  микрофона и динамика между звонками, и `SIPUserAgent.AttendedTransfer(SIPDialogue, …)`.
  На FreeSWITCH серверный attended недоступен в принципе — драйвер бросает
  `UnsupportedByPbxError` с текстом про то, что софтфон делает это через SIP REFER, —
  значит реализация обязана быть клиентской.
- Живая статистика очередей (ожидающие, свободные операторы) в списке.
- Сужение списка по департаменту (ADR-020).
- Правка веб-диалога перевода под `supervisorPausedBy` — отдельная задача.

## 9. Замечено попутно, чинится в рамках работы

Дыры 2, 3 и 5 из раздела 2 закрываются здесь, потому что этот код всё равно трогается.
Дыра 4 в веб-диалоге не трогается — другой репозиторий, другой экран, отдельная задача.
