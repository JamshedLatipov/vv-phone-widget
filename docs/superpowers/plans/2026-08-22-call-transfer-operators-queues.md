# Перевод звонка на операторов и очереди — план реализации (фаза 1, blind)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** оператор в виджете выбирает цель перевода из списка операторов или очередей своей организации, и бэк переводит звонок.

**Целевые ветки:** `main` в `../crm_mono` и `main` в `vv-phone-widget`. **Не** `feat/freeswitch-all` — см. раздел «Перенос на feat/freeswitch-all» в конце.

**Architecture:** бэк получает per-org диалплан-контекст `org-<uuid>-queues`, ARI-перевод учится отправлять абонента в очередь, появляется лёгкий эндпоинт списка целей с проверкой принадлежности организации. Виджет получает сервис, чистый презентер и панель с двумя вкладками, с падением обратно на текущий SIP REFER, когда бэк недоступен.

**Tech Stack:** NestJS + TypeORM + vitest (`crm_mono`), .NET 8 + Avalonia + xUnit (`vv-phone-widget`), Asterisk ARI.

**Спек:** `docs/superpowers/specs/2026-08-22-call-transfer-operators-queues-design.md` — писался под драйверную абстракцию `feat/freeswitch-all`. Этот план реализует ту же фичу в идиоме main; расхождения перечислены ниже.

---

## Почему план не совпадает со спеком

Спек проектировался по рабочему дереву, которое стояло на `feat/freeswitch-all`. На `origin/main` фундамента из спека нет:

| Что | `feat/freeswitch-all` | `origin/main` |
|---|---|---|
| `TelephonyCallControlDriver` | есть | **нет** |
| `channel-locator.ts` / `subscriberLeg` | есть | **нет** |
| `POST /calls/transfer` | `{callId, targetExtension}`, драйвер | `{channelId, target}`, ARI `CallTransferService` |
| `contextForOrg` | `sip-trunk/dialplan-context.util.ts`, тип `DialplanContextKind` | `sip-trunk/services/sip-trunk.service.ts:264`, инлайновый юнион |
| Изоляция источника перевода | `subscriberLeg` + endpoint из токена | `assertChannelBelongsToTenant` по контексту канала |
| Presence board с `supervisorPausedBy` | есть | **есть** |
| `GET /contact-center/queues` под `calls:read` | есть | **есть** |
| `GET /cdr/channel-uniqueid` | есть | **есть** |

`feat/freeswitch-all` — 333 коммита впереди main и 70 позади. Различается только слой «как дёргаем АТС». Контекст очередей, эндпоинт целей, проверка организации, аудит и **весь виджет** — общие.

---

## Как это шипится

Пять слайсов. Каждый — отдельный PR в `main`, зелёный сам по себе, мержится и деплоится **не дожидаясь остальных**.

| Слайс | Репозиторий | Что ломает | Мерж в main |
|---|---|---|---|
| 1. Контекст очередей | `crm_mono` | ничего — только новые строки диалплана | сразу |
| 2. ARI-перевод в очередь | `crm_mono` | ничего — новый параметр с дефолтом | сразу |
| 3. Список целей и валидация | `crm_mono` | ничего — новый эндпоинт плюс ужесточение неиспользуемого | сразу |
| 4. Сервис и презентер виджета | `vv-phone-widget` | ничего — код без вызывающих | сразу |
| 5. UI виджета | `vv-phone-widget` | ничего — деградирует при 404/403 | сразу |

**Почему ужесточение `POST /api/calls/transfer` безопасно.** У эндпоинта нет потребителей и на main тоже: веб-софтфон переводит звонок клиентски, через JsSIP REFER (`apps/front/src/app/softphone/softphone.service.ts:426`), а `CallsApiService.transfer` не вызывается ниоткуда. Супервайзерский перевод идёт своим маршрутом — `POST /api/contact-center/supervisor/transfer` — и не трогается.

**Порядок деплоя.** Слайсы 1–3 на прод раньше, чем операторам уедет инсталлятор со слайсом 5. Мержить можно в любом порядке: слайс 5 при отсутствующем эндпоинте показывает старое поле ручного ввода, ровно как сегодня.

**Что откладывается:** attended-перевод (фаза 2), живая статистика очередей, сужение по департаменту, правка веб-диалога под `supervisorPausedBy`.

---

## Структура файлов

### `crm_mono` (ветка от `origin/main`)

| Файл | Ответственность |
|---|---|
| `apps/back/src/app/modules/sip-trunk/dialplan-context.util.ts` | создать: `DialplanContextKind` с `'queues'`, `contextForOrg`, `assertContextForOrg` — без entity-импортов |
| `apps/back/src/app/modules/sip-trunk/services/sip-trunk.service.ts:31,264` | правка: убрать оригиналы, переэкспортировать |
| `apps/back/src/app/modules/sip-trunk/index.ts` | правка: публичный вход в новый модуль |
| `apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.ts` | создать: чистые строки контекста очередей |
| `apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.vitest.ts` | создать: тест на них |
| `apps/back/src/app/modules/pjsip/services/operator-provisioning.service.ts:164,290` | правка: вызов `ensureQueueTransferContext` |
| `apps/back/src/app/modules/calls/migrations/20260822000000-AddQueueTransferContext.ts` | создать: бэкфилл по существующим организациям |
| `apps/back/src/app/modules/calls/services/call-transfer.service.ts` | правка: перевод в очередь |
| `apps/back/src/app/modules/calls/services/call-transfer.service.vitest.ts` | создать: тесты на выбор цели |
| `apps/back/src/app/modules/calls/services/transfer-target.service.ts` | создать: список целей и проверка принадлежности |
| `apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts` | создать: тесты на неё |
| `apps/back/src/app/modules/calls/controllers/calls.controller.ts` | правка: `GET transfer-targets`, `targetKind`, аудит |
| `apps/back/src/app/modules/calls/calls.module.ts` | правка: `PresenceModule`, `Queue`, новый сервис |

### `vv-phone-widget` (ветка от `main`)

| Файл | Ответственность |
|---|---|
| `OrbitalSIP/Models/TransferModels.cs` | создать: DTO целей, запроса и результата |
| `OrbitalSIP/Services/TransferTargetsPresenter.cs` | создать: чистая логика состояний и фильтра |
| `OrbitalSIP/Services/TransferService.cs` | создать: HTTP плюс статические парсеры |
| `OrbitalSIP.Tests/TransferTargetsPresenterTests.cs` | создать |
| `OrbitalSIP.Tests/TransferServiceTests.cs` | создать |
| `OrbitalSIP/Assets/i18n/{ru,kk,tg,uz}.json` | правка: новые ключи |
| `OrbitalSIP/Views/ActiveCallView.axaml` | правка: панель с вкладками |
| `OrbitalSIP/Views/ActiveCallView.axaml.cs` | правка: загрузка, отрисовка, событие |
| `OrbitalSIP/MainWindow.axaml.cs` | правка: маршрутизация бэк / SIP-фолбэк |

---

# Слайс 1 — контекст очередей (`crm_mono`)

### Task 1: вынести `contextForOrg` из-под entity и научить его контексту `queues`

**Files:**
- Create: `apps/back/src/app/modules/sip-trunk/dialplan-context.util.ts`
- Modify: `apps/back/src/app/modules/sip-trunk/services/sip-trunk.service.ts:31,264-290`
- Modify: `apps/back/src/app/modules/sip-trunk/index.ts`

**Почему не просто дописать `'queues'` в юнион.** `contextForOrg` живёт в
`sip-trunk.service.ts`, который импортирует сущность `SipTrunk`. Под vitest,
где не эмитится `design:type`, этот импорт бросает
`ColumnTypeUndefinedError: Column type for SipTrunk#name is not defined` **на
сборке файла**, и весь тестовый файл не собирается. Это уже происходит на
`origin/main`: `ps-endpoint.service.vitest.ts` падает именно так. Task 5 добавит
`contextForOrg` в `CallTransferService` и утащит туда же свой новый тест.
Поэтому функции переезжают в модуль без entity-импортов — ровно тот же файл и
то же имя, что уже есть на `feat/freeswitch-all`, так что мерж потом сойдётся
сам.

- [ ] **Step 1: Убедиться, что базовое падение — предсуществующее**

Run: `cd /c/work/crm_mono/.worktrees/call-transfer-targets && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/pjsip/services 2>&1 | grep -E "FAIL|Test Files"`
Expected: `FAIL … ps-endpoint.service.vitest.ts`, `1 failed | 2 passed`. Это состояние main до правок — запомнить, чтобы сравнить после.

- [ ] **Step 2: Создать модуль без entity-импортов**

`apps/back/src/app/modules/sip-trunk/dialplan-context.util.ts`:

```ts
import { BadRequestException } from '@nestjs/common';

/**
 * ADR-007 per-organization dialplan context names.
 *
 * These two functions used to live at the bottom of
 * `services/sip-trunk.service.ts`, under a comment saying they were free
 * functions "so unit tests don't need to spin up the service". That was half
 * the problem: the file they lived in imports the `SipTrunk` entity, so every
 * importer of the helper dragged TypeORM decorator metadata into its module
 * graph. Under vitest — which does not emit `design:type` — that import throws
 * `ColumnTypeUndefinedError: Column type for SipTrunk#name is not defined` at
 * load time, and the whole test FILE fails to collect.
 *
 * Nothing here touches a database, a repository or an entity — keep it that
 * way, or the breakage comes back.
 */

export type DialplanContextKind =
  | 'internal'
  | 'from-trunk'
  | 'outbound'
  // Куда `Redirect` уводит абонента при переводе в очередь. Собственный
  // контекст, а не строка в `internal`, потому что здесь живёт catch-all `_.`:
  // в `internal` он перехватил бы каждый набранный номер. Сюда нельзя попасть
  // с эндпоинта — только явным редиректом от сервиса перевода.
  | 'queues';

/**
 * Read once at load, as it was in the service. A rollback-friendly rollout
 * switch, not a per-request setting.
 */
const PER_ORG_CONTEXTS_FLAG_ENABLED =
  process.env.FEATURE_TELEPHONY_PER_ORG_CONTEXTS !== 'false';

/** The per-org dialplan context name for one organization and direction. */
export function contextForOrg(
  organizationId: string,
  kind: DialplanContextKind,
): string {
  return `org-${organizationId}-${kind}`;
}

/**
 * Throws if `context` doesn't match the per-org convention for this org.
 * Skipped when the feature flag is off — useful for rollback-friendly rollouts.
 */
export function assertContextForOrg(
  context: string,
  organizationId: string,
  kind: DialplanContextKind,
): void {
  if (!PER_ORG_CONTEXTS_FLAG_ENABLED) return;
  const expected = contextForOrg(organizationId, kind);
  if (context !== expected) {
    throw new BadRequestException(
      `Dialplan context must equal "${expected}" but was "${context}"`,
    );
  }
}
```

- [ ] **Step 3: Убрать оригиналы из сервиса и переэкспортировать**

В `sip-trunk.service.ts`: удалить `const PER_ORG_CONTEXTS_FLAG_ENABLED` (строка 31) и обе функции внизу файла. Вместо них — импорт для внутреннего употребления и переэкспорт, чтобы существующие `import { contextForOrg } from '../../sip-trunk'` не сломались:

```ts
import {
  assertContextForOrg,
  contextForOrg,
} from '../dialplan-context.util';

// Переэкспорт: модульный `index.ts` делает `export * from
// './services/sip-trunk.service'`, и без этой строки каждый существующий
// импортёр пришлось бы править в этом же коммите.
export { assertContextForOrg, contextForOrg } from '../dialplan-context.util';
export type { DialplanContextKind } from '../dialplan-context.util';
```

Если `BadRequestException` после удаления функций больше нигде в файле не используется — убрать его из импортов.

- [ ] **Step 4: Дать модулю прямой публичный вход**

В `apps/back/src/app/modules/sip-trunk/index.ts` добавить строку:

```ts
export * from './dialplan-context.util';
```

- [ ] **Step 5: Проверить сборку и отсутствие регрессий**

Run: `cd /c/work/crm_mono/.worktrees/call-transfer-targets && npx tsc -p apps/back/tsconfig.app.json --noEmit && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/sip-trunk apps/back/src/app/modules/dialplan 2>&1 | tail -6`
Expected: tsc без ошибок; `Test Files 5 passed`, `Tests 58 passed`.

`ps-endpoint.service.vitest.ts` на этом шаге **всё ещё красный, и это правильно** — см. Step 7.

- [ ] **Step 6: Коммит**

```bash
git add apps/back/src/app/modules/sip-trunk/
git commit -m "feat(dialplan): a queues context kind, in a module free of entity imports"
```

- [ ] **Step 7: Направить сломанного импортёра мимо барреля**

Переезд функций сам по себе `ps-endpoint.service.vitest.ts` не чинит, и вот почему: `ps-endpoint.service.ts:8` берёт `contextForOrg` из **барреля** `'../../sip-trunk'`, а `index.ts` обязан продолжать делать `export * from './services/sip-trunk.service'` — иначе не экспортируется сам `SipTrunkService`. По семантике ES-модулей `export * from X` вычисляет `X` целиком при загрузке агрегатора, лениво по именам это не работает. То есть импорт чего угодно через баррель по-прежнему грузит сервис, а с ним и сущность.

Правка — одна строка в `apps/back/src/app/modules/pjsip/services/ps-endpoint.service.ts`:

```ts
// Deep import on purpose: the barrel re-exports `sip-trunk.service`, whose
// entity import makes any vitest file that loads it fail to collect.
import { contextForOrg } from '../../sip-trunk/dialplan-context.util';
```

Правило `no-restricted-imports` этому не мешает: оно закрывает `sip-trunk/services/*`, а `dialplan-context.util.ts` лежит на верхнем уровне модуля — ровно ради такого импорта.

Остальные шесть баррель-импортёров `contextForOrg` (`operator-provisioning.service.ts`, `campaign-dialer.service.ts`, `dialplan-scenario.service.ts`, `inbound-routing.service.ts`, `outbound-routing.service.ts`, `ivr-runtime.service.ts`) не трогать: ни у одного нет vitest-файла, который грузит настоящий класс сервиса, так что правка была бы churn без падающего теста.

Run: `cd /c/work/crm_mono/.worktrees/call-transfer-targets && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/pjsip/services 2>&1 | tail -6`
Expected: `Test Files 3 passed` — вот теперь `ps-endpoint.service.vitest.ts` собирается.

```bash
git add apps/back/src/app/modules/pjsip/services/ps-endpoint.service.ts
git commit -m "fix(pjsip): reach the dialplan helper without loading the sip-trunk barrel"
```

**Следствие для Task 5:** новый импорт в `CallTransferService` обязан идти по прямому пути `'../../sip-trunk/dialplan-context.util'`. Через баррель его новый vitest-файл не соберётся.

---

### Task 2: чистые строки контекста очередей

**Files:**
- Create: `apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.ts`
- Test: `apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.vitest.ts`

- [ ] **Step 1: Написать падающий тест**

```ts
import { describe, expect, it } from 'vitest';
import { queueTransferRows } from './queue-transfer.dialplan';

describe('queueTransferRows', () => {
  it('sends whatever extension it was redirected to into Queue()', () => {
    const rows = queueTransferRows();
    expect(rows.find((r) => r.app === 'Queue')).toEqual({
      exten: '_.',
      priority: '1',
      app: 'Queue',
      appdata: '${EXTEN},n',
    });
  });

  it('hangs up after the queue instead of falling through', () => {
    expect(queueTransferRows().at(-1)).toEqual({
      exten: '_.',
      priority: '2',
      app: 'Hangup',
      appdata: null,
    });
  });

  it('keeps the hangup path out of Queue()', () => {
    const h = queueTransferRows().find((r) => r.exten === 'h');
    expect(h?.app).toBe('NoOp');
    expect(queueTransferRows().filter((r) => r.exten === 'h' && r.app === 'Queue')).toHaveLength(0);
  });

  it('is exactly three rows — a fourth would need a priority renumber', () => {
    expect(queueTransferRows()).toHaveLength(3);
  });
});
```

**Про строку `h`.** `_.` матчит **любой** экстеншн, включая спец-экстеншны Asterisk. Реально срабатывает `h`: когда абонент кладёт трубку, стоя в очереди — самый обычный конец брошенного перевода, — Asterisk ищет `h` в том же контексте, шаблон её ловит, и приоритет 1 выполняет `Queue(h,n)` по очереди с именем `h`, которой нет. Каналу уже всё равно, но предупреждение падает в лог на каждом таком звонке у каждой организации. Явный экстеншн бьёт шаблон, поэтому строка `h` с `NoOp` закрывает вопрос. `Hangup` в `h` не нужен — канал и так разрушается.

Заменить `_.` на `_X.` **нельзя**: имена очередей буквенные (`sales`), а `_X.` требует ведущей цифры.

**Про `,n`.** Опция значит «не повторять обзвон по таймауту, выйти на следующий приоритет» — именно она делает строку `Hangup` достижимой. К выходу абонента по DTMF отношения не имеет: тот управляется полем `context` самой очереди.

**Про пустую очередь — известное ограничение фазы 1.** И колоночный дефолт (`queue.entity.ts:42`), и путь создания через админку (`queue-drawer.component.ts:189`, `startCreate()`) ставят `leavewhenempty: true`. При нём Asterisk выкидывает абонента из очереди, когда в ней нет залогиненных участников либо все на паузе/недоступны — **не** просто когда все заняты разговором. После этого `Queue()` завершается, следующий приоритет — наш `Hangup`, и абонент разъединяется вместо ожидания.

То есть перевод в очередь, у которой все агенты на перерыве, кладёт трубку абоненту. Виджет в фазе 1 живую статистику очередей не тянет и такую очередь от прочих не отличает. Закрывается пометкой недоступных очередей в `listTargets` — вне объёма фазы 1.

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.vitest.ts`
Expected: FAIL — `Failed to resolve import "./queue-transfer.dialplan"`.

- [ ] **Step 3: Минимальная реализация**

```ts
/**
 * Строки диалплана для per-org контекста `queues` — точки приземления перевода
 * звонка в очередь.
 *
 * Отдельный модуль без entity-импортов, по той же причине, что и
 * `operator-provisioning.dialplan.ts`: под vitest импорт TypeORM-сущности
 * роняет сбор всего файла.
 *
 * Catch-all `_.` здесь безопасен, а в `internal` был бы катастрофой: туда
 * попадают набранные оператором номера, и `_.` перехватил бы их все. В этот
 * контекст нельзя попасть с эндпоинта — только явным редиректом из
 * `CallTransferService`, который уже проверил, что имя очереди принадлежит
 * организации звонящего.
 *
 * Но `_.` матчит и спец-экстеншны Asterisk, а `h` ищется в этом же контексте,
 * когда абонент кладёт трубку из очереди. Без явной строки `h` шаблон её
 * перехватит и выполнит `Queue(h,n)` по несуществующей очереди на каждом
 * брошенном переводе. Явный экстеншн бьёт шаблон — за этим строка `h` и стоит,
 * удалять её нельзя.
 */
import type { DialplanRow } from './operator-provisioning.dialplan';

export function queueTransferRows(): DialplanRow[] {
  return [
    // Разрыв связи со стороны абонента. `NoOp`, а не `Hangup`: канал и так
    // разрушается, а вот `Queue(h,n)` по очереди с именем `h` — нет.
    { exten: 'h', priority: '1', app: 'NoOp', appdata: 'Queue transfer hangup' },
    // `,n` — не повторять обзвон по таймауту, а выйти на следующий приоритет.
    // Именно она делает строку `Hangup` достижимой. DTMF тут ни при чём: выход
    // абонента по клавише управляется полем `context` самой очереди.
    { exten: '_.', priority: '1', app: 'Queue', appdata: '${EXTEN},n' },
    // Срабатывает при плохом имени очереди и при переполненной. При пустой —
    // нет: таймаут не передаётся, а фикстуры ставят `leavewhenempty=no`.
    // Без этой строки канал вываливается из контекста и Asterisk продолжает
    // разбирать диалплан там, где его никто не ждёт.
    { exten: '_.', priority: '2', app: 'Hangup', appdata: null },
  ];
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.vitest.ts`
Expected: PASS, 3 теста.

Если `DialplanRow` не экспортируется из `operator-provisioning.dialplan.ts` — добавить `export` к его объявлению `interface DialplanRow`.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.ts apps/back/src/app/modules/pjsip/services/queue-transfer.dialplan.vitest.ts
git commit -m "feat(dialplan): rows for the per-org queue transfer context"
```

---

### Task 3: провижининг контекста при заведении оператора

**Files:**
- Modify: `apps/back/src/app/modules/pjsip/services/operator-provisioning.service.ts:164,290`

- [ ] **Step 1: Добавить импорт**

```ts
import { queueTransferRows } from './queue-transfer.dialplan';
```

- [ ] **Step 2: Добавить метод рядом с `ensureInternalOutboundRoute` (строка 290)**

```ts
  /**
   * Заводит per-org контекст `queues` — точку приземления перевода звонка в
   * очередь. Идемпотентно и realtime-only: Asterisk читает диалплан из базы на
   * каждый вызов, reload не нужен.
   */
  private async ensureQueueTransferContext(
    organizationId: string,
    manager: EntityManager,
  ): Promise<void> {
    const context = contextForOrg(organizationId, 'queues');
    for (const row of queueTransferRows()) {
      await manager.query(
        `INSERT INTO "extensions"
           ("context","exten","priority","app","appdata","description","organization_id")
         VALUES ($1,$2,$3,$4,$5,$6,$7)
         ON CONFLICT ("context","exten","priority") DO NOTHING`,
        [
          context,
          row.exten,
          row.priority,
          row.app,
          row.appdata,
          'queue transfer landing (per-org queues)',
          organizationId,
        ],
      );
    }
  }
```

- [ ] **Step 3: Вызвать его рядом со строкой 164**

После `await this.ensureInternalOutboundRoute(organizationId, manager);` добавить:

```ts
    await this.ensureQueueTransferContext(organizationId, manager);
```

- [ ] **Step 4: Проверить сборку и провижининговые тесты**

Run: `cd /c/work/crm_mono && npx tsc -p apps/back/tsconfig.app.json --noEmit && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/pjsip/services`
Expected: без ошибок, PASS.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/pjsip/services/operator-provisioning.service.ts
git commit -m "feat(pjsip): provision the queue transfer context per organization"
```

---

### Task 4: миграция для существующих организаций

**Files:**
- Create: `apps/back/src/app/modules/calls/migrations/20260822000000-AddQueueTransferContext.ts`

- [ ] **Step 1: Убедиться, что номер миграции сортируется последним**

Run: `cd /c/work/crm_mono && git ls-tree -r --name-only origin/main -- 'apps/back/src/app/modules/*/migrations' | sed 's#.*/##' | grep -o '^[0-9]\+' | sort -n | tail -3`
Expected: наибольший — `20251024013923`. Выбранный `20260822000000` больше него, значит применится последним. Если в выводе появилось что-то большее — поднять номер.

- [ ] **Step 2: Написать миграцию**

```ts
import { MigrationInterface, QueryRunner } from 'typeorm';

/**
 * Заводит per-org контекст `queues` для организаций, которые уже существуют.
 *
 * `OperatorProvisioningService` делает это для новых, но у действующих
 * организаций контекста нет, а перевод звонка в очередь без него уходит в
 * никуда. Организации берутся из уже существующих `org-<uuid>-internal`
 * контекстов: у организации без телефонии переводить всё равно нечего.
 *
 * Идемпотентно (`ON CONFLICT DO NOTHING`) и realtime-only — reload Asterisk не
 * нужен, диалплан читается из базы на каждый вызов.
 *
 * Держать в синхроне с `queueTransferRows()`.
 */
export class AddQueueTransferContext20260822000000 implements MigrationInterface {
  name = 'AddQueueTransferContext20260822000000';

  // exten, priority, app, appdata — держать в синхроне с `queueTransferRows()`.
  // Строка `h` не декоративная: `_.` матчит и спец-экстеншны Asterisk, так что
  // без явного `h` разрыв связи со стороны абонента выполнял бы `Queue(h,n)`
  // по несуществующей очереди на каждом брошенном переводе.
  private static readonly ROWS: Array<[string, string, string, string | null]> = [
    ['h', '1', 'NoOp', 'Queue transfer hangup'],
    ['_.', '1', 'Queue', '${EXTEN},n'],
    ['_.', '2', 'Hangup', null],
  ];

  async up(qr: QueryRunner): Promise<void> {
    const orgs: Array<{ organization_id: string | null }> = await qr.query(
      `SELECT DISTINCT organization_id
         FROM extensions
        WHERE context LIKE 'org-%-internal'
          AND organization_id IS NOT NULL`,
    );

    for (const { organization_id: organizationId } of orgs) {
      const context = `org-${organizationId}-queues`;
      for (const [
        exten,
        priority,
        app,
        appdata,
      ] of AddQueueTransferContext20260822000000.ROWS) {
        await qr.query(
          `INSERT INTO "extensions"
             ("context","exten","priority","app","appdata","description","organization_id")
           VALUES ($1,$2,$3,$4,$5,$6,$7)
           ON CONFLICT ("context","exten","priority") DO NOTHING`,
          [
            context,
            exten,
            priority,
            app,
            appdata,
            'queue transfer landing (per-org queues)',
            organizationId,
          ],
        );
      }
    }
  }

  async down(qr: QueryRunner): Promise<void> {
    await qr.query(`DELETE FROM "extensions" WHERE context LIKE 'org-%-queues'`);
  }
}
```

- [ ] **Step 3: Прогнать миграции на локальной базе**

Run: `cd /c/work/crm_mono && npm run migration:run`
Expected: `AddQueueTransferContext20260822000000` применилась. Если скрипт называется иначе — взять имя из `MIGRATIONS.md`.

- [ ] **Step 4: Проверить результат запросом**

Run: `psql "$DATABASE_URL" -c "SELECT context, exten, priority, app, appdata FROM extensions WHERE context LIKE 'org-%-queues' ORDER BY context, priority LIMIT 10"`
Expected: по две строки `_.` на организацию — `Queue ${EXTEN},n` и `Hangup`.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/calls/migrations/20260822000000-AddQueueTransferContext.ts
git commit -m "feat(dialplan): backfill the queue transfer context for existing orgs"
```

**Слайс 1 готов к мержу в main.**

---

# Слайс 2 — ARI-перевод в очередь (`crm_mono`)

### Task 5: `CallTransferService` учится отправлять в очередь

**Files:**
- Modify: `apps/back/src/app/modules/calls/services/call-transfer.service.ts`
- Test: `apps/back/src/app/modules/calls/services/call-transfer.service.vitest.ts`

- [ ] **Step 1: Написать падающий тест**

```ts
import { describe, expect, it, vi } from 'vitest';
import { CallTransferService } from './call-transfer.service';

function rig(orgId: string | null) {
  const redirect = vi.fn(async () => undefined);
  const client = {
    channels: {
      get: vi.fn(async () => ({ dialplan: { context: 'from-trunk' } })),
      redirect,
      originate: vi.fn(async () => ({ id: 'new' })),
    },
  };
  const service = new CallTransferService(
    { getClient: () => client } as never,
    { currentOrganizationId: () => orgId } as never,
  );
  return { service, redirect };
}

describe('CallTransferService.blindTransfer', () => {
  it('sends a queue target into the per-org queues context', async () => {
    const { service, redirect } = rig('org-1');

    await service.blindTransfer('ch-1', 'sales', 'queue');

    expect(redirect).toHaveBeenCalledWith({
      channelId: 'ch-1',
      endpoint: 'Local/sales@org-org-1-queues',
    });
  });

  it('leaves an extension target on the existing ari context', async () => {
    const { service, redirect } = rig('org-1');

    await service.blindTransfer('ch-1', '1042', 'extension');

    const endpoint = redirect.mock.calls[0][0].endpoint as string;
    expect(endpoint).not.toContain('queues');
    expect(endpoint).toContain('1042');
  });

  it('refuses a queue transfer with no organization rather than guessing a context', async () => {
    const { service } = rig(null);

    await expect(service.blindTransfer('ch-1', 'sales', 'queue')).rejects.toThrow(
      /organization/i,
    );
  });

  it('defaults to an extension so existing callers keep their meaning', async () => {
    const { service, redirect } = rig('org-1');

    await service.blindTransfer('ch-1', '1042');

    expect(redirect.mock.calls[0][0].endpoint).not.toContain('queues');
  });
});
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/calls/services/call-transfer.service.vitest.ts`
Expected: FAIL — `blindTransfer` не принимает третий аргумент, редирект уходит в `from-ari`.

- [ ] **Step 3: Правка сервиса**

Добавить импорт. **Именно из `dialplan-context.util`, не из `../../sip-trunk`** — второй тянет `SipTrunk` entity и роняет сборку нового vitest-файла (см. Task 1):

```ts
import { contextForOrg } from '../../sip-trunk/dialplan-context.util';
```

Заменить `blindTransfer` и `attendedTransfer`:

```ts
  /**
   * Куда именно уводить абонента.
   *
   * Имя очереди не является набираемым экстеншном ни в одном контексте:
   * `Queue()` генерится только для DID/IVR/flow-веток. Поэтому очередь уходит
   * в собственный per-org контекст, чей catch-all передаёт `${EXTEN}` в
   * `Queue()`. Без организации подставить его неоткуда, и молчаливый фолбэк в
   * общий контекст увёл бы абонента в чужой диалплан.
   */
  private endpointFor(
    target: string,
    targetKind: 'extension' | 'queue',
  ): string {
    if (target.includes('/')) return target;

    if (targetKind === 'queue') {
      const orgId = this.tenantService.currentOrganizationId();
      if (!orgId)
        throw new ForbiddenException(
          `Cannot transfer to queue ${target}: no organization on this call`,
        );
      return `Local/${target}@${contextForOrg(orgId, 'queues')}`;
    }

    const aricontext = process.env.ASTERISK_FROM_ARI_CONTEXT || 'from-ari';
    return `Local/${target}@${aricontext}`;
  }

  async blindTransfer(
    channelId: string,
    target: string,
    targetKind: 'extension' | 'queue' = 'extension',
  ) {
    const client = this.ari.getClient();
    if (!client) throw new Error('ARI client not available');
    await this.assertChannelBelongsToTenant(channelId);
    this.logger.log(`Blind transfer ${channelId} -> ${targetKind} ${target}`);

    const normalized = this.endpointFor(target, targetKind);

    try {
      await client.channels.redirect({ channelId, endpoint: normalized });
    } catch (err) {
      this.logger.warn(
        `redirect failed, trying originate as fallback: ${err instanceof Error ? err.message : String(err)}`,
      );
      try {
        const appName = process.env.ARI_APP || 'crm-app';
        const originateParams = {
          endpoint: normalized,
          app: appName,
          callerId: channelId,
        };
        this.logger.debug(
          `ARI originate params (call-transfer fallback): ${JSON.stringify(originateParams)}`,
        );
        await client.channels.originate(originateParams);
      } catch (e) {
        this.logger.error('Blind transfer failed', e as Error);
        throw e;
      }
    }
  }

  async attendedTransfer(
    channelId: string,
    target: string,
    targetKind: 'extension' | 'queue' = 'extension',
  ) {
    const client = this.ari.getClient();
    if (!client) throw new Error('ARI client not available');
    await this.assertChannelBelongsToTenant(channelId);
    this.logger.log(`Attended transfer ${channelId} -> ${targetKind} ${target}`);
    try {
      const bridge = await client.bridges.create({ type: 'mixing' });
      const appName = process.env.ARI_APP || 'crm-app';
      const ep = this.endpointFor(target, targetKind);
      const orig = await client.channels.originate({
        endpoint: ep,
        app: appName,
        callerId: channelId,
      });
      const newChannelId =
        orig && (orig.id || (orig.channel && orig.channel.id));
      if (newChannelId) {
        await client.bridges.addChannel({
          bridgeId: bridge.id,
          channel: [channelId, newChannelId],
        });
      } else {
        this.logger.warn(
          'Unable to determine new channel id for attended transfer'
        );
      }
    } catch (err) {
      this.logger.error('Attended transfer failed', err as Error);
      throw err;
    }
  }
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/calls/services/call-transfer.service.vitest.ts`
Expected: PASS, 4 теста.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/calls/services/call-transfer.service.ts apps/back/src/app/modules/calls/services/call-transfer.service.vitest.ts
git commit -m "feat(calls): send a queue transfer into the per-org queues context"
```

**Слайс 2 готов к мержу в main.**

---

# Слайс 3 — список целей, валидация, аудит (`crm_mono`)

### Task 6: `TransferTargetService.listTargets`

**Files:**
- Create: `apps/back/src/app/modules/calls/services/transfer-target.service.ts`
- Test: `apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts`

- [ ] **Step 1: Написать падающий тест**

```ts
import { describe, expect, it, vi } from 'vitest';
import { TransferTargetService } from './transfer-target.service';

const BOARD = [
  { userId: 1, fullName: 'Свободный', sipEndpointId: '1042', sipRegistered: true, onCall: false, manualStatus: null, supervisorPausedBy: null },
  { userId: 2, fullName: 'На звонке', sipEndpointId: '1043', sipRegistered: true, onCall: true, manualStatus: null, supervisorPausedBy: null },
  { userId: 3, fullName: 'На перерыве', sipEndpointId: '1044', sipRegistered: true, onCall: false, manualStatus: 'break', supervisorPausedBy: null },
  { userId: 4, fullName: 'Пауза супервайзера', sipEndpointId: '1045', sipRegistered: true, onCall: false, manualStatus: null, supervisorPausedBy: 9 },
  { userId: 5, fullName: 'Не зарегистрирован', sipEndpointId: '1046', sipRegistered: false, onCall: false, manualStatus: null, supervisorPausedBy: null },
  { userId: 6, fullName: 'Я сам', sipEndpointId: '1099', sipRegistered: true, onCall: true, manualStatus: null, supervisorPausedBy: null },
];

function build(queues: Array<{ name: string; description?: string | null }>) {
  return new TransferTargetService(
    { getBoard: vi.fn(async () => BOARD) } as never,
    { find: vi.fn(async () => queues), findOne: vi.fn(async () => null) } as never,
    { findOne: vi.fn(async () => null) } as never,
    { applyScope: (where: object) => where } as never,
  );
}

describe('TransferTargetService.listTargets', () => {
  it('offers only operators a call can actually reach', async () => {
    const { operators } = await build([]).listTargets('1099');
    expect(operators.map((o) => o.extension)).toEqual(['1042']);
  });

  it('never offers the operator themselves', async () => {
    const { operators } = await build([]).listTargets('1042');
    expect(operators).toHaveLength(0);
  });

  it('marks a queue whose name the PBX wire cannot carry instead of hiding it', async () => {
    const { queues } = await build([
      { name: 'sales', description: 'Продажи' },
      { name: 'Отдел продаж', description: null },
    ]).listTargets('1099');

    expect(queues).toEqual([
      { name: 'sales', description: 'Продажи', disabledReason: null },
      { name: 'Отдел продаж', description: null, disabledReason: 'unsafe-name' },
    ]);
  });
});
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts`
Expected: FAIL — `Failed to resolve import "./transfer-target.service"`.

- [ ] **Step 3: Реализация**

```ts
import { ForbiddenException, Injectable } from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository } from 'typeorm';
import { TenantService } from '@crm/server-tenant';
import { PresenceBoardService } from '../../presence/presence-board.service';
import { Queue } from '../../cc-queues/entities/queue.entity';
import { User } from '../../user/user.entity';

/**
 * Что можно положить в строку команды АТС. Имя очереди — свободный `text` в
 * базе, а на провод уходит куском строки ARI/AMI, где пробел начинает второй
 * аргумент. Очередь «Отдел продаж» существовать может, достижимой переводом —
 * нет.
 */
const PBX_SAFE = /^[A-Za-z0-9_.@-]+$/;

export type QueueDisabledReason = 'unsafe-name';

export interface TransferOperatorTarget {
  extension: string;
  fullName: string;
}

export interface TransferQueueTarget {
  name: string;
  description: string | null;
  disabledReason: QueueDisabledReason | null;
}

export interface TransferTargets {
  operators: TransferOperatorTarget[];
  queues: TransferQueueTarget[];
}

/**
 * Куда оператору можно перевести звонок — и проверка, что названная им цель
 * действительно оттуда.
 *
 * Список и проверка живут в одном классе намеренно. Раздельно они разъезжаются:
 * список сузили, проверку забыли, и цель, которой в UI нет, всё равно
 * принимается запросом.
 */
@Injectable()
export class TransferTargetService {
  constructor(
    private readonly board: PresenceBoardService,
    @InjectRepository(Queue)
    private readonly queueRepo: Repository<Queue>,
    @InjectRepository(User)
    private readonly userRepo: Repository<User>,
    private readonly tenantService: TenantService,
  ) {}

  async listTargets(ownExtension: string | null): Promise<TransferTargets> {
    const [rows, queues] = await Promise.all([
      this.board.getBoard(),
      this.queueRepo.find({
        select: ['name', 'description'],
        where: this.tenantService.applyScope({}, this.queueRepo.metadata),
      }),
    ]);

    return {
      // `supervisorPausedBy` проверяется наравне с `manualStatus`: оператор
      // свою принудительную паузу не снимает, и `manualStatus` при ней
      // остаётся null — по нему одному такая пауза не видна.
      operators: rows
        .filter(
          (r) =>
            r.sipEndpointId != null &&
            r.sipEndpointId !== ownExtension &&
            r.sipRegistered &&
            !r.onCall &&
            r.manualStatus === null &&
            r.supervisorPausedBy === null,
        )
        .map((r) => ({
          extension: r.sipEndpointId as string,
          fullName: r.fullName,
        })),
      queues: queues.map((q) => ({
        name: q.name,
        description: q.description ?? null,
        disabledReason: PBX_SAFE.test(q.name) ? null : ('unsafe-name' as const),
      })),
    };
  }
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts`
Expected: PASS, 3 теста.

Если `PresenceBoardRow` на main не отдаёт `fullName` — взять имя тем же способом, каким его собирает `presence-board.service.ts` в своём `getBoard`.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/calls/services/transfer-target.service.ts apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts
git commit -m "feat(calls): list the transfer targets an operator may reach"
```

---

### Task 7: `assertTargetAllowed`

**Files:**
- Modify: `apps/back/src/app/modules/calls/services/transfer-target.service.ts`
- Modify: `apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts`

- [ ] **Step 1: Дописать падающие тесты**

```ts
function guard(queue: object | null, user: object | null) {
  return new TransferTargetService(
    { getBoard: vi.fn(async () => []) } as never,
    { find: vi.fn(async () => []), findOne: vi.fn(async () => queue) } as never,
    { findOne: vi.fn(async () => user) } as never,
    { applyScope: (where: object) => where } as never,
  );
}

describe('TransferTargetService.assertTargetAllowed', () => {
  it('refuses a queue that is not this organization\'s', async () => {
    await expect(
      guard(null, null).assertTargetAllowed('queue', 'sales'),
    ).rejects.toThrow(/queue/i);
  });

  it('refuses an extension that belongs to nobody in this organization', async () => {
    await expect(
      guard(null, null).assertTargetAllowed('extension', '1042'),
    ).rejects.toThrow(/extension/i);
  });

  it('accepts a queue of this organization', async () => {
    await expect(
      guard({ name: 'sales' }, null).assertTargetAllowed('queue', 'sales'),
    ).resolves.toBeUndefined();
  });

  it('accepts an extension of this organization', async () => {
    await expect(
      guard(null, { id: 7, sipEndpointId: '1042' }).assertTargetAllowed('extension', '1042'),
    ).resolves.toBeUndefined();
  });
});
```

- [ ] **Step 2: Запустить и убедиться, что падает**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts`
Expected: FAIL — `assertTargetAllowed is not a function`.

- [ ] **Step 3: Дописать метод**

```ts
  /**
   * Цель принадлежит организации звонящего — или запрос отвергается.
   *
   * До этого метода цель не проверял никто. `assertChannelBelongsToTenant`
   * изолирует ИСТОЧНИК — канал чужой организации отвергается по контексту
   * диалплана, — но `target` уезжал в ARI как есть, и валидный по символам
   * экстеншн чужой организации проходил насквозь.
   */
  async assertTargetAllowed(
    targetKind: 'extension' | 'queue',
    value: string,
  ): Promise<void> {
    if (!PBX_SAFE.test(value))
      throw new ForbiddenException(`Unusable target ${JSON.stringify(value)}`);

    if (targetKind === 'queue') {
      const queue = await this.queueRepo.findOne({
        where: this.tenantService.applyScope(
          { name: value },
          this.queueRepo.metadata,
        ),
      });
      if (!queue)
        throw new ForbiddenException(`Unknown queue ${JSON.stringify(value)}`);
      return;
    }

    const user = await this.userRepo.findOne({
      where: this.tenantService.applyScope(
        { sipEndpointId: value },
        this.userRepo.metadata,
      ),
    });
    if (!user)
      throw new ForbiddenException(`Unknown extension ${JSON.stringify(value)}`);
  }
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `cd /c/work/crm_mono && npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts`
Expected: PASS, 7 тестов.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/calls/services/transfer-target.service.ts apps/back/src/app/modules/calls/services/transfer-target.service.vitest.ts
git commit -m "fix(calls): a transfer target must belong to the caller's organization"
```

---

### Task 8: контроллер и модуль

**Files:**
- Modify: `apps/back/src/app/modules/calls/calls.module.ts`
- Modify: `apps/back/src/app/modules/calls/controllers/calls.controller.ts`

- [ ] **Step 1: Правка модуля**

Добавить импорты:

```ts
import { PresenceModule } from '../presence/presence.module';
import { AuditLogModule } from '../audit-log/audit-log.module';
import { Queue } from '../cc-queues/entities/queue.entity';
import { TransferTargetService } from './services/transfer-target.service';
```

В `imports` добавить `PresenceModule` и `AuditLogModule`, в `TypeOrmModule.forFeature([...])` — `Queue`, в `providers` — `TransferTargetService`.

**Решение, которое надо принять здесь осознанно.** Контроллер заворачивает любое исключение в `catch (err) { return { ok: false, error: (err as Error).message } }` с кодом 200. После проводки `targetKind` через него туда начнут попадать два новых класса отказов: `BadRequestException` от `attendedTransfer` при `targetKind === 'queue'` и `ForbiddenException` от `assertTargetAllowed` при чужой цели. Оба потеряют статус и приедут клиенту как 200.

Для виджета это работает: `TransferService.ParseTransferResult` читает тело, а не код, и любой `{ok:false}` даёт `Failed` — то есть SIP-фолбэк не включится, что и требуется, раз бэкенд ответил отказом осознанно. Но решение надо зафиксировать комментарием, а не унаследовать молча: сейчас неотличимость статусов безвредна ровно потому, что единственный потребитель смотрит на тело.

- [ ] **Step 2: Правка контроллера**

Заменить файл целиком:

```ts
import { Body, Controller, Get, Post, UseGuards } from '@nestjs/common';
import { ApiTags } from '@nestjs/swagger';
import { CallTransferService } from '../services/call-transfer.service';
import { TransferTargetService } from '../services/transfer-target.service';
import { AbilityGuard } from '../../rbac/guards/ability.guard';
import { CheckAbility } from '../../rbac/decorators/check-ability.decorator';
import { RequireTenantGuard } from '../../../common/guards/require-tenant.guard';
import {
  CurrentUser,
  CurrentUserPayload,
} from '../../user/current-user.decorator';
import { AuditLogService } from '../../audit-log/audit-log.service';

@ApiTags('Calls')
@UseGuards(AbilityGuard, RequireTenantGuard)
@Controller('calls')
export class CallsController {
  constructor(
    private readonly transferSvc: CallTransferService,
    private readonly targets: TransferTargetService,
    private readonly auditLog: AuditLogService,
  ) {}

  /**
   * Where this operator may send the call.
   *
   * Gated on `calls:create` rather than `presence:read` — the same ability the
   * transfer itself needs. An account that cannot transfer has no use for the
   * list, and two different gates on the two halves of one action drift.
   */
  @Get('transfer-targets')
  @CheckAbility('calls:create')
  async transferTargets(@CurrentUser() user: CurrentUserPayload) {
    return this.targets.listTargets(user.operator?.username ?? null);
  }

  /**
   * Transfer a live call.
   *
   * The SOURCE is isolated by `assertChannelBelongsToTenant` — a channel whose
   * dialplan context names another org is refused. The TARGET is isolated
   * separately, by `assertTargetAllowed`: before it, `target` went into ARI
   * verbatim and nothing checked whose extension or queue it was.
   */
  @Post('transfer')
  @CheckAbility('calls:create')
  async doTransfer(
    @Body()
    body: {
      channelId: string;
      target: string;
      targetKind?: 'extension' | 'queue';
      type?: 'blind' | 'attended';
    },
    @CurrentUser() user: CurrentUserPayload,
  ) {
    if (!body?.channelId || !body.target)
      return { ok: false, error: 'missing params' };

    const targetKind = body.targetKind === 'queue' ? 'queue' : 'extension';

    try {
      await this.targets.assertTargetAllowed(targetKind, body.target);
      if (body.type === 'attended')
        await this.transferSvc.attendedTransfer(
          body.channelId,
          body.target,
          targetKind,
        );
      else
        await this.transferSvc.blindTransfer(
          body.channelId,
          body.target,
          targetKind,
        );
      // Ключ `calls.transfer` уже есть в audit-log; супервайзерский путь его
      // пишет, операторский — нет. Awaited по той же причине, что и там:
      // read-after-write должен видеть строку. `record()` сам по себе
      // error-safe и не роняет действие, которое логирует.
      await this.auditLog.record({
        action: 'calls.transfer',
        resourceType: 'call',
        resourceId: body.channelId,
        metadata: {
          target: body.target,
          targetKind,
          type: body.type === 'attended' ? 'attended' : 'blind',
        },
        actorUserId: user?.sub ?? null,
        actorName: user?.username ?? null,
        organizationId: user?.organizationId ?? null,
      });
      return { ok: true };
    } catch (err) {
      return { ok: false, error: (err as Error).message };
    }
  }
}
```

- [ ] **Step 3: Проверить сборку и весь бэковый набор**

Run: `cd /c/work/crm_mono && npx tsc -p apps/back/tsconfig.app.json --noEmit && npm run test:vitest:back`
Expected: без ошибок, PASS.

- [ ] **Step 4: Проверить эндпоинт руками**

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:3000/api/calls/transfer-targets
```

Expected: `{"operators":[…],"queues":[…]}`. Токен без `calls:create` → 403.

- [ ] **Step 5: Коммит**

```bash
git add apps/back/src/app/modules/calls/calls.module.ts apps/back/src/app/modules/calls/controllers/calls.controller.ts
git commit -m "feat(calls): expose transfer targets, honour a queue target, audit the transfer"
```

**Слайс 3 готов к мержу в main. После деплоя фича полностью рабочая через curl.**

---

# Слайс 4 — сервис и презентер виджета (`vv-phone-widget`)

> **Комментарии в `.cs` — английской прозой.** Русский допустим только внутри
> закавыченных UI-строк и в тестовых данных. Образец — `LeadCallPanelPresenter.cs`
> и `LeadCallPanelPresenterTests.cs`.
>
> Блоки кода в задачах 9, 10 и 13 ниже написаны с русскими комментариями — это
> ошибка автора плана, вскрытая на ревью Task 9. Смысл комментариев сохранять
> дословно (они фиксируют *почему*, а не *что*), а прозу переводить на английский.
>
> **И ещё одно, общее для всех wire-типов виджета.** `System.Text.Json` на .NET 8
> кладёт `null` в non-nullable ссылочное свойство, когда ключ в ответе
> отсутствует: `RespectNullableAnnotations` выключен и нигде в проекте не
> настроен. Поэтому список, приходящий с бэка, объявляется nullable, а пустоту
> подставляет презентер. Тот же приём, что у `LeadCallContext` в
> `Models/LeadModels.cs` — там каждое такое поле дефолтится ровно по этой причине.

### Task 9: модели и презентер

**Files:**
- Create: `OrbitalSIP/Models/TransferModels.cs`
- Create: `OrbitalSIP/Services/TransferTargetsPresenter.cs`
- Test: `OrbitalSIP.Tests/TransferTargetsPresenterTests.cs`

- [ ] **Step 1: Завести ветку**

```bash
cd /c/work/vv-phone-widget && git checkout main && git pull && git checkout -b feat/call-transfer-targets
```

- [ ] **Step 2: Написать падающий тест**

```csharp
using System.Collections.Generic;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Пустой список целей и недоступный бэк выглядят в UI одинаково, если их не
/// различать состоянием: в первом случае переводить действительно некому, во
/// втором — виджету просто не ответили, и оператору нужна кнопка повтора.
/// </summary>
public class TransferTargetsPresenterTests
{
    private static TransferTargets Targets(
        IReadOnlyList<TransferOperatorTarget>? operators = null,
        IReadOnlyList<TransferQueueTarget>? queues = null) =>
        new(operators ?? [], queues ?? []);

    [Fact]
    public void Loading_BeatsEverythingElse()
    {
        Assert.Equal(
            TransferPanelState.Loading,
            TransferTargetsPresenter.SelectState(null, loading: true, error: null, forbidden: false));
    }

    [Fact]
    public void Forbidden_IsNotAnError_SoNoRetryIsOffered()
    {
        Assert.Equal(
            TransferPanelState.Forbidden,
            TransferTargetsPresenter.SelectState(null, loading: false, error: null, forbidden: true));
    }

    [Fact]
    public void EmptyTargets_IsDistinctFromAFailedLoad()
    {
        Assert.Equal(
            TransferPanelState.Empty,
            TransferTargetsPresenter.SelectState(Targets(), loading: false, error: null, forbidden: false));
        Assert.Equal(
            TransferPanelState.Error,
            TransferTargetsPresenter.SelectState(null, loading: false, error: "boom", forbidden: false));
    }

    [Fact]
    public void Filter_ExcludesTheOperatorThemselves()
    {
        var targets = Targets(operators: [
            new TransferOperatorTarget("1042", "Свободный"),
            new TransferOperatorTarget("1099", "Я сам"),
        ]);

        var rows = TransferTargetsPresenter.FilterOperators(targets, query: "", ownExtension: "1099");

        Assert.Single(rows);
        Assert.Equal("1042", rows[0].Extension);
    }

    [Fact]
    public void Filter_MatchesNameAndExtensionCaseInsensitively()
    {
        var targets = Targets(operators: [
            new TransferOperatorTarget("1042", "Иванов"),
            new TransferOperatorTarget("2050", "Петров"),
        ]);

        Assert.Single(TransferTargetsPresenter.FilterOperators(targets, "иван", ownExtension: null));
        Assert.Single(TransferTargetsPresenter.FilterOperators(targets, "2050", ownExtension: null));
    }

    [Fact]
    public void Filter_SinksUndialableQueuesToTheBottomInsteadOfDroppingThem()
    {
        var targets = Targets(queues: [
            new TransferQueueTarget("Отдел продаж", null, "unsafe-name"),
            new TransferQueueTarget("sales", "Продажи", null),
        ]);

        var rows = TransferTargetsPresenter.FilterQueues(targets, query: "");

        Assert.Equal(2, rows.Count);
        Assert.Equal("sales", rows[0].Name);
        Assert.Equal("Отдел продаж", rows[1].Name);
    }

    [Fact]
    public void QueueDisabledKey_MapsEveryKnownReasonAndFallsBack()
    {
        Assert.Null(TransferTargetsPresenter.QueueDisabledKey(null));
        Assert.Equal("TransferQueueUnsafeName", TransferTargetsPresenter.QueueDisabledKey("unsafe-name"));
        Assert.Equal("TransferQueueUnavailable", TransferTargetsPresenter.QueueDisabledKey("something-new"));
    }
}
```

- [ ] **Step 3: Запустить и убедиться, что не собирается**

Run: `cd /c/work/vv-phone-widget && dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: FAIL — `The type or namespace name 'TransferTargets' could not be found`.

- [ ] **Step 4: Написать модели**

`OrbitalSIP/Models/TransferModels.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrbitalSIP.Models
{
    /// <summary>Оператор, которому можно отдать звонок. `Extension` — набираемый номер.</summary>
    public sealed record TransferOperatorTarget(
        [property: JsonPropertyName("extension")] string Extension,
        [property: JsonPropertyName("fullName")] string FullName);

    /// <summary>
    /// Очередь. <c>DisabledReason</c> непустой означает, что бэк её нашёл, но
    /// перевести туда нельзя — например, имя очереди не проходит по проводу
    /// АТС. Такую строку показываем серой, а не прячем: короткий список без
    /// объяснения читается как «очередей нет».
    /// </summary>
    public sealed record TransferQueueTarget(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("disabledReason")] string? DisabledReason);

    public sealed record TransferTargets(
        [property: JsonPropertyName("operators")] IReadOnlyList<TransferOperatorTarget> Operators,
        [property: JsonPropertyName("queues")] IReadOnlyList<TransferQueueTarget> Queues);

    /// <summary>Что именно оператор выбрал. Определяет, каким путём уйдёт перевод.</summary>
    public enum TransferTargetKind
    {
        /// <summary>Номер оператора. При недоступном бэке уходит локальным SIP REFER.</summary>
        Extension,

        /// <summary>Имя очереди. Локального фолбэка нет — REFER на имя очереди уедет в никуда.</summary>
        Queue,
    }

    /// <summary>
    /// Исход попытки перевода. <c>ChannelUnresolved</c> отделён от <c>Failed</c>
    /// намеренно: именно он, и только он, включает SIP-фолбэк для оператора.
    /// </summary>
    public enum TransferOutcome
    {
        Ok,
        Failed,
        ChannelUnresolved,
    }

    public sealed record TransferResult(TransferOutcome Outcome, string? Error);

    /// <summary>
    /// Что оператор выбрал и по какому номеру бэк найдёт живой канал.
    /// `CallerNumber` — номер второй стороны; по нему резолвится channelId.
    /// </summary>
    public sealed record TransferRequest(TransferTargetKind Kind, string Value, string CallerNumber);
}
```

- [ ] **Step 5: Написать презентер**

`OrbitalSIP/Services/TransferTargetsPresenter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>Что панель перевода показывает прямо сейчас.</summary>
    public enum TransferPanelState
    {
        Loading,

        /// <summary>Загрузка не удалась. Предлагаем повтор.</summary>
        Error,

        /// <summary>403 или 404 от бэка. Повтор не поможет — остаётся ручной ввод.</summary>
        Forbidden,

        /// <summary>Бэк ответил, но переводить некому.</summary>
        Empty,

        Ready,
    }

    /// <summary>
    /// Вся логика панели перевода, которую можно проверить без окна.
    /// Тот же приём, что и в <see cref="LeadCallPanelPresenter"/>: вид только
    /// рисует то, что решили здесь.
    /// </summary>
    public static class TransferTargetsPresenter
    {
        /// <summary>Подсказка для очереди, которую бэк пометил недоступной, но причину назвал незнакомую.</summary>
        public const string QueueUnavailableKey = "TransferQueueUnavailable";

        public static TransferPanelState SelectState(
            TransferTargets? targets,
            bool loading,
            string? error,
            bool forbidden)
        {
            if (loading) return TransferPanelState.Loading;
            if (forbidden) return TransferPanelState.Forbidden;
            if (targets == null) return TransferPanelState.Error;
            if (!string.IsNullOrEmpty(error)) return TransferPanelState.Error;
            return targets.Operators.Count == 0 && targets.Queues.Count == 0
                ? TransferPanelState.Empty
                : TransferPanelState.Ready;
        }

        public static IReadOnlyList<TransferOperatorTarget> FilterOperators(
            TransferTargets? targets,
            string query,
            string? ownExtension)
        {
            if (targets == null) return [];
            var q = (query ?? string.Empty).Trim();

            return targets.Operators
                .Where(o => !string.Equals(o.Extension, ownExtension, StringComparison.OrdinalIgnoreCase))
                .Where(o => q.Length == 0 || Matches(o.FullName, q) || Matches(o.Extension, q))
                .OrderBy(o => o.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<TransferQueueTarget> FilterQueues(
            TransferTargets? targets,
            string query)
        {
            if (targets == null) return [];
            var q = (query ?? string.Empty).Trim();

            return targets.Queues
                .Where(x => q.Length == 0 || Matches(x.Name, q) || Matches(x.Description, q))
                // Недоступные — вниз, а не прочь: оператор должен увидеть, что
                // очередь есть, и почему она серая.
                .OrderBy(x => string.IsNullOrEmpty(x.DisabledReason) ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// i18n-ключ подсказки для недоступной очереди, или null если очередь
        /// доступна. Незнакомая причина не остаётся без текста — иначе
        /// добавленное на бэке значение обернулось бы пустой подсказкой.
        /// </summary>
        public static string? QueueDisabledKey(string? disabledReason) =>
            string.IsNullOrWhiteSpace(disabledReason)
                ? null
                : disabledReason switch
                {
                    "unsafe-name" => "TransferQueueUnsafeName",
                    _ => QueueUnavailableKey,
                };

        private static bool Matches(string? value, string query) =>
            !string.IsNullOrEmpty(value)
            && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
```

- [ ] **Step 6: Запустить и убедиться, что проходит**

Run: `cd /c/work/vv-phone-widget && dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter TransferTargetsPresenterTests`
Expected: PASS, 7 тестов.

- [ ] **Step 7: Коммит**

```bash
git add OrbitalSIP/Models/TransferModels.cs OrbitalSIP/Services/TransferTargetsPresenter.cs OrbitalSIP.Tests/TransferTargetsPresenterTests.cs
git commit -m "feat(transfer): pure state and filtering for the transfer target panel"
```

---

### Task 10: `TransferService`

**Files:**
- Create: `OrbitalSIP/Services/TransferService.cs`
- Test: `OrbitalSIP.Tests/TransferServiceTests.cs`

- [ ] **Step 1: Написать падающий тест**

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Xunit;

namespace OrbitalSIP.Tests;

/// <summary>
/// Перевод — двухшаговый: сначала резолвится channelId, потом уходит POST. Если
/// шаги перепутать или пропустить первый, бэк получит запрос без канала и
/// ответит отказом, который оператору не объяснить. Плюс `{ok:false}` приходит
/// с HTTP 200 — без разбора тела успех и провал неразличимы.
/// </summary>
public class TransferServiceTests
{
    [Fact]
    public void ParseTargets_ReadsBothListsAndTheDisabledReason()
    {
        var targets = TransferService.ParseTargets("""
        {
          "operators": [{ "extension": "1042", "fullName": "Иванов" }],
          "queues": [
            { "name": "sales", "description": "Продажи", "disabledReason": null },
            { "name": "Отдел продаж", "description": null, "disabledReason": "unsafe-name" }
          ]
        }
        """);

        Assert.NotNull(targets);
        Assert.Equal("1042", targets!.Operators[0].Extension);
        Assert.Null(targets.Queues[0].DisabledReason);
        Assert.Equal("unsafe-name", targets.Queues[1].DisabledReason);
    }

    [Fact]
    public void ParseTargets_ReturnsNullOnGarbageRatherThanThrowing()
    {
        Assert.Null(TransferService.ParseTargets("not json"));
    }

    [Fact]
    public void ParseTransferResult_TreatsOkFalseAsFailureDespiteHttp200()
    {
        var result = TransferService.ParseTransferResult("""{ "ok": false, "error": "Unknown queue" }""");

        Assert.Equal(TransferOutcome.Failed, result.Outcome);
        Assert.Equal("Unknown queue", result.Error);
    }

    [Fact]
    public void ParseTransferResult_ReadsSuccess()
    {
        Assert.Equal(TransferOutcome.Ok, TransferService.ParseTransferResult("""{ "ok": true }""").Outcome);
    }

    [Fact]
    public async Task TransferAsync_ResolvesTheChannelBeforePosting()
    {
        var captured = new List<HttpRequestMessage>();
        using var handler = new RecordingHandler(request =>
        {
            captured.Add(request);
            return request.RequestUri!.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : JsonResponse("""{ "ok": true }""");
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.Ok, result.Outcome);
        Assert.Equal(2, captured.Count);
        Assert.Contains("channel-uniqueid", captured[0].RequestUri!.AbsoluteUri);
        Assert.Contains("/api/calls/transfer", captured[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task TransferAsync_SendsTheQueueKindOnTheWire()
    {
        string? body = null;
        using var handler = new RecordingHandler(request =>
        {
            if (!request.RequestUri!.AbsolutePath.Contains("channel-uniqueid"))
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return request.RequestUri.AbsolutePath.Contains("channel-uniqueid")
                ? JsonResponse("""{ "uniqueid": "1719990000.42" }""")
                : JsonResponse("""{ "ok": true }""");
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        await service.TransferAsync(
            TransferTargetKind.Queue, "sales", "+992900000000", CancellationToken.None);

        Assert.Contains("\"targetKind\":\"queue\"", body);
        Assert.Contains("\"target\":\"sales\"", body);
        Assert.Contains("\"channelId\":\"1719990000.42\"", body);
    }

    [Fact]
    public async Task TransferAsync_ReportsAnUnresolvedChannelDistinctlySoTheCallerCanFallBack()
    {
        using var handler = new RecordingHandler(_ => JsonResponse("""{ }"""));
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var result = await service.TransferAsync(
            TransferTargetKind.Extension, "1042", "+992900000000", CancellationToken.None);

        Assert.Equal(TransferOutcome.ChannelUnresolved, result.Outcome);
    }

    [Fact]
    public async Task GetTargetsAsync_SurfacesForbiddenSeparatelyFromAFailedLoad()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        using var client = new HttpClient(handler);
        using var service = new TransferService(client, Settings, ownsHttpClient: false);

        var response = await service.GetTargetsAsync(CancellationToken.None);

        Assert.True(response.Forbidden);
        Assert.Null(response.Targets);
    }

    private static Func<SipSettings> Settings => () => new SipSettings
    {
        BackendUrl = "https://crm.example/",
        AccessToken = "widget-token",
    };

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что не собирается**

Run: `cd /c/work/vv-phone-widget && dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter TransferServiceTests`
Expected: FAIL — `The type or namespace name 'TransferService' could not be found`.

- [ ] **Step 3: Написать сервис**

`OrbitalSIP/Services/TransferService.cs`:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    /// <summary>Ответ на запрос списка целей. 403 отделён от ошибки: повтор его не лечит.</summary>
    public sealed record TransferTargetsResponse(TransferTargets? Targets, bool Forbidden, string? Error);

    /// <summary>
    /// Перевод звонка через бэк: он один знает, достижима ли цель и принадлежит
    /// ли она организации оператора, и он один умеет отправить абонента в
    /// очередь — имя очереди не является набираемым номером ни в одном
    /// контексте, так что SIP REFER на него уходит в никуда.
    /// </summary>
    public sealed class TransferService : IDisposable
    {
        /// <summary>
        /// Панель перевода висит поверх активного звонка: стодесятисекундный
        /// дефолт HttpClient читается оператором как зависший софтфон.
        /// </summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient _httpClient;
        private readonly Func<SipSettings> _settingsProvider;
        private readonly bool _ownsHttpClient;

        // `BackendHttp.CreateClient`, НЕ `new HttpClient()`. Второй обходит общий
        // `AuthRefreshHandler` — панель работала бы до истечения токена, а дальше
        // молча получала 401 без единой попытки обновления. Плюс поднимал бы
        // собственный пул сокетов, ради устранения которых `BackendHttp` и написан.
        // Образец — `FlowsService.cs:26`.
        public TransferService()
            : this(BackendHttp.CreateClient(RequestTimeout),
                   () => App.SipService?.CurrentSettings ?? SipSettings.Load(),
                   ownsHttpClient: true)
        {
        }

        public TransferService(HttpClient httpClient, Func<SipSettings> settingsProvider, bool ownsHttpClient)
        {
            _httpClient = httpClient;
            _settingsProvider = settingsProvider;
            _ownsHttpClient = ownsHttpClient;
            if (ownsHttpClient) _httpClient.Timeout = RequestTimeout;
        }

        public TimeSpan HttpClientTimeoutForTests => _httpClient.Timeout;

        public async Task<TransferTargetsResponse> GetTargetsAsync(CancellationToken ct = default)
        {
            var settings = _settingsProvider();
            var backendUrl = settings.BackendUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                return new TransferTargetsResponse(null, Forbidden: false, Error: "no backend configured");

            var url = $"{backendUrl}/api/calls/transfer-targets";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
                using var response = await _httpClient.SendAsync(request, ct);

                // 403 и 404 значат одно: этому виджету списка не будет. 404 —
                // бэк ещё не обновлён, 403 — роли не хватает права. И там и там
                // остаётся ручной ввод, и повтор ничего не изменит.
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                    return new TransferTargetsResponse(null, Forbidden: true, Error: null);

                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log("TransferService", $"transfer-targets failed. Status: {response.StatusCode}. Body: {body}");
                    return new TransferTargetsResponse(null, Forbidden: false, Error: response.StatusCode.ToString());
                }

                var targets = ParseTargets(body);
                return targets == null
                    ? new TransferTargetsResponse(null, Forbidden: false, Error: "unreadable response")
                    : new TransferTargetsResponse(targets, Forbidden: false, Error: null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log("TransferService", $"transfer-targets exception: {ex.GetType().Name}: {ex.Message}");
                return new TransferTargetsResponse(null, Forbidden: false, Error: ex.Message);
            }
        }

        /// <summary>
        /// Резолвит channelId по номеру второй стороны и просит бэк перевести.
        /// </summary>
        public async Task<TransferResult> TransferAsync(
            TransferTargetKind kind,
            string target,
            string callerNumber,
            CancellationToken ct = default)
        {
            var settings = _settingsProvider();
            var backendUrl = settings.BackendUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                return new TransferResult(TransferOutcome.ChannelUnresolved, "no backend configured");

            try
            {
                var channelId = await ResolveChannelIdAsync(backendUrl, settings.AccessToken!, callerNumber, ct);
                if (string.IsNullOrWhiteSpace(channelId))
                    return new TransferResult(TransferOutcome.ChannelUnresolved, null);

                var payload = JsonSerializer.Serialize(new
                {
                    channelId,
                    target,
                    targetKind = kind == TransferTargetKind.Queue ? "queue" : "extension",
                    type = "blind",
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{backendUrl}/api/calls/transfer")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Log("TransferService", $"transfer failed. Status: {response.StatusCode}. Body: {body}");
                    return new TransferResult(TransferOutcome.Failed, response.StatusCode.ToString());
                }

                return ParseTransferResult(body);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Log("TransferService", $"transfer exception: {ex.GetType().Name}: {ex.Message}");
                return new TransferResult(TransferOutcome.Failed, ex.Message);
            }
        }

        private async Task<string?> ResolveChannelIdAsync(
            string backendUrl,
            string accessToken,
            string callerNumber,
            CancellationToken ct)
        {
            var url = $"{backendUrl}/api/cdr/channel-uniqueid?callerNumber={Uri.EscapeDataString(callerNumber)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("uniqueid", out var value)
                    ? value.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Null означает «ответ не разобрать», а не «целей нет».</summary>
        public static TransferTargets? ParseTargets(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<TransferTargets>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Бэк отвечает <c>{ok:false,error}</c> с кодом 200 — без разбора тела
        /// отказ выглядит как успех, и звонок «переводится» в никуда.
        /// </summary>
        public static TransferResult ParseTransferResult(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var ok = document.RootElement.TryGetProperty("ok", out var okValue)
                         && okValue.ValueKind == JsonValueKind.True;
                if (ok) return new TransferResult(TransferOutcome.Ok, null);

                var error = document.RootElement.TryGetProperty("error", out var errorValue)
                    ? errorValue.GetString()
                    : null;
                return new TransferResult(TransferOutcome.Failed, error);
            }
            catch (JsonException)
            {
                return new TransferResult(TransferOutcome.Failed, null);
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }
}
```

- [ ] **Step 4: Запустить и убедиться, что проходит**

Run: `cd /c/work/vv-phone-widget && dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter TransferServiceTests`
Expected: PASS, 8 тестов.

- [ ] **Step 5: Прогнать весь набор**

Run: `cd /c/work/vv-phone-widget && dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Коммит**

```bash
git add OrbitalSIP/Services/TransferService.cs OrbitalSIP.Tests/TransferServiceTests.cs
git commit -m "feat(transfer): ask the backend to move the call, and read its answer"
```

**Слайс 4 готов к мержу в main — код без вызывающих, но зелёный и покрытый.**

---

# Слайс 5 — UI виджета (`vv-phone-widget`)

### Task 11: строки локализации

**Files:**
- Modify: `OrbitalSIP/Assets/i18n/ru.json`, `kk.json`, `tg.json`, `uz.json`

- [ ] **Step 1: `ru.json`**

```json
  "TransferTabOperators": "Операторы",
  "TransferTabQueues": "Очереди",
  "TransferSearch": "Поиск...",
  "TransferNoTargets": "Некому перевести",
  "TransferLoadFailed": "Не удалось загрузить список",
  "TransferRetry": "Повторить",
  "TransferQueueUnsafeName": "Имя очереди не поддерживается АТС",
  "TransferQueueUnavailable": "Очередь недоступна для перевода",
  "TransferDone": "Звонок переведён",
  "TransferFailed": "Не удалось перевести звонок",
```

- [ ] **Step 2: `kk.json`**

```json
  "TransferTabOperators": "Операторлар",
  "TransferTabQueues": "Кезектер",
  "TransferSearch": "Іздеу...",
  "TransferNoTargets": "Аударуға ешкім жоқ",
  "TransferLoadFailed": "Тізімді жүктеу мүмкін болмады",
  "TransferRetry": "Қайталау",
  "TransferQueueUnsafeName": "Кезек атауын АТС қолдамайды",
  "TransferQueueUnavailable": "Кезекке аудару қолжетімсіз",
  "TransferDone": "Қоңырау аударылды",
  "TransferFailed": "Қоңырауды аудару мүмкін болмады",
```

- [ ] **Step 3: `tg.json`**

```json
  "TransferTabOperators": "Операторҳо",
  "TransferTabQueues": "Навбатҳо",
  "TransferSearch": "Ҷустуҷӯ...",
  "TransferNoTargets": "Касе барои гузаронидан нест",
  "TransferLoadFailed": "Рӯйхатро бор кардан нашуд",
  "TransferRetry": "Такрор",
  "TransferQueueUnsafeName": "Номи навбатро АТС дастгирӣ намекунад",
  "TransferQueueUnavailable": "Навбат барои гузаронидан дастрас нест",
  "TransferDone": "Занг гузаронида шуд",
  "TransferFailed": "Зангро гузаронидан нашуд",
```

- [ ] **Step 4: `uz.json`**

```json
  "TransferTabOperators": "Operatorlar",
  "TransferTabQueues": "Navbatlar",
  "TransferSearch": "Qidiruv...",
  "TransferNoTargets": "O'tkazadigan odam yo'q",
  "TransferLoadFailed": "Ro'yxatni yuklab bo'lmadi",
  "TransferRetry": "Qayta urinish",
  "TransferQueueUnsafeName": "Navbat nomini ATS qo'llab-quvvatlamaydi",
  "TransferQueueUnavailable": "Navbatga o'tkazish mumkin emas",
  "TransferDone": "Qo'ng'iroq o'tkazildi",
  "TransferFailed": "Qo'ng'iroqni o'tkazib bo'lmadi",
```

- [ ] **Step 5: Проверить, что все четыре файла — валидный JSON и ключи совпадают**

Run: `cd /c/work/vv-phone-widget && for f in OrbitalSIP/Assets/i18n/*.json; do echo "$f: $(python -c "import json,sys;d=json.load(open(sys.argv[1],encoding='utf-8'));print(len([k for k in d if k.startswith('Transfer')]))" "$f")"; done`
Expected: одно и то же число во всех четырёх строках.

- [ ] **Step 6: Коммит**

```bash
git add OrbitalSIP/Assets/i18n/
git commit -m "i18n(transfer): strings for the target picker in all four locales"
```

---

### Task 12: панель с вкладками в XAML

**Files:**
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml:116-146`

- [ ] **Step 1: Заменить `<StackPanel Spacing="8">` внутри `<Border Name="TransferPanel" …>`**

```xml
            <StackPanel Spacing="8">
              <TextBlock Text="{i18n:I18n TransferToOperator}" FontSize="9" FontWeight="Bold"
                         LetterSpacing="1.4" Foreground="#7B92AA" />

              <Grid ColumnDefinitions="*,*" Name="TransferTabs">
                <Button Name="TransferTabOperatorsBtn"
                        Height="28" CornerRadius="8,0,0,8"
                        Background="#1E4270" BorderThickness="0"
                        Foreground="#DDE7F3" FontSize="11" FontWeight="Bold"
                        HorizontalAlignment="Stretch" HorizontalContentAlignment="Center"
                        Content="{i18n:I18n TransferTabOperators}" />
                <Button Name="TransferTabQueuesBtn"
                        Grid.Column="1"
                        Height="28" CornerRadius="0,8,8,0"
                        Background="#152132" BorderThickness="0"
                        Foreground="#7B92AA" FontSize="11" FontWeight="Bold"
                        HorizontalAlignment="Stretch" HorizontalContentAlignment="Center"
                        Content="{i18n:I18n TransferTabQueues}" />
              </Grid>

              <TextBox Name="TransferSearchBox"
                       Background="#152132" BorderBrush="#1D3050" BorderThickness="1"
                       CornerRadius="10" Padding="10,6" FontSize="12"
                       Foreground="#F8FAFC" CaretBrush="#60A5FA"
                       Watermark="{i18n:I18n TransferSearch}" />

              <TextBlock Name="TransferStatusLabel" IsVisible="False"
                         FontSize="11" Foreground="#7B92AA" TextWrapping="Wrap" />

              <Button Name="TransferRetryBtn" IsVisible="False"
                      Height="28" CornerRadius="8"
                      Background="#1E4270" BorderThickness="0"
                      Foreground="#60A5FA" FontSize="11" FontWeight="Bold"
                      HorizontalAlignment="Stretch" HorizontalContentAlignment="Center"
                      Content="{i18n:I18n TransferRetry}" />

              <!-- Виджет узкий и невысокий: без потолка список выдавливает
                   кнопки управления звонком за край окна. -->
              <ScrollViewer Name="TransferListScroll" MaxHeight="150"
                            VerticalScrollBarVisibility="Auto">
                <StackPanel Name="TransferList" Spacing="4" />
              </ScrollViewer>

              <Grid ColumnDefinitions="*,Auto">
                <TextBox Name="TransferNumberBox"
                         Margin="0,0,8,0"
                         Background="#152132"
                         BorderBrush="#1D3050"
                         BorderThickness="1"
                         CornerRadius="10"
                         Padding="10,6"
                         FontSize="13"
                         Foreground="#F8FAFC"
                         CaretBrush="#60A5FA"
                         Watermark="{i18n:I18n OperatorNumber}" />
                <Button Name="TransferConfirmBtn"
                        Grid.Column="1"
                        Width="74" Height="34"
                        CornerRadius="10"
                        Background="#1E4270"
                        BorderThickness="0"
                        Foreground="#60A5FA"
                        FontSize="11"
                        FontWeight="Bold"
                        Content="{i18n:I18n Transfer}"
                        HorizontalContentAlignment="Center" />
              </Grid>
            </StackPanel>
```

- [ ] **Step 2: Проверить, что XAML компилируется**

Run: `cd /c/work/vv-phone-widget && dotnet build OrbitalSIP/OrbitalSIP.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Коммит**

```bash
git add OrbitalSIP/Views/ActiveCallView.axaml
git commit -m "feat(transfer): tabs, search and a target list in the transfer panel"
```

---

### Task 13: код-бихайнд панели

**Files:**
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml.cs`

- [ ] **Step 1: Добавить поля**

```csharp
        private readonly TransferService _transferService = new();
        private TransferTargets? _transferTargets;
        private bool _transferLoading;
        private bool _transferForbidden;
        private string? _transferError;
        private bool _transferQueuesTab;
```

- [ ] **Step 2: Подписки рядом с `TransferConfirmBtn` (около строки 265)**

```csharp
            var transferTabOperators = this.FindControl<Button>("TransferTabOperatorsBtn");
            if (transferTabOperators != null)
                transferTabOperators.Click += (_, __) => SelectTransferTab(queues: false);

            var transferTabQueues = this.FindControl<Button>("TransferTabQueuesBtn");
            if (transferTabQueues != null)
                transferTabQueues.Click += (_, __) => SelectTransferTab(queues: true);

            var transferSearch = this.FindControl<TextBox>("TransferSearchBox");
            if (transferSearch != null)
                transferSearch.TextChanged += (_, __) => RenderTransferList();

            var transferRetry = this.FindControl<Button>("TransferRetryBtn");
            if (transferRetry != null)
                transferRetry.Click += SafeHandler.Click("Transfer", LoadTransferTargetsAsync);
```

- [ ] **Step 3: Загрузка при открытии панели**

В `ShowTransferPanel()`, после того как панель стала видимой:

```csharp
            _ = LoadTransferTargetsAsync();
```

И метод:

```csharp
        private async Task LoadTransferTargetsAsync()
        {
            _transferLoading = true;
            _transferError = null;
            _transferForbidden = false;
            RenderTransferList();

            var response = await _transferService.GetTargetsAsync();

            _transferLoading = false;
            _transferTargets = response.Targets;
            _transferForbidden = response.Forbidden;
            _transferError = response.Error;
            RenderTransferList();
        }
```

- [ ] **Step 4: Переключение вкладки**

```csharp
        private void SelectTransferTab(bool queues)
        {
            _transferQueuesTab = queues;

            var operators = this.FindControl<Button>("TransferTabOperatorsBtn");
            var queuesBtn = this.FindControl<Button>("TransferTabQueuesBtn");
            if (operators != null)
            {
                operators.Background = new SolidColorBrush(Color.Parse(queues ? "#152132" : "#1E4270"));
                operators.Foreground = new SolidColorBrush(Color.Parse(queues ? "#7B92AA" : "#DDE7F3"));
            }
            if (queuesBtn != null)
            {
                queuesBtn.Background = new SolidColorBrush(Color.Parse(queues ? "#1E4270" : "#152132"));
                queuesBtn.Foreground = new SolidColorBrush(Color.Parse(queues ? "#DDE7F3" : "#7B92AA"));
            }

            RenderTransferList();
        }
```

- [ ] **Step 5: Отрисовка списка**

```csharp
        /// <summary>
        /// Вся ветвистость решена в <see cref="TransferTargetsPresenter"/> —
        /// здесь только рисование. Ветку «что показать» держать тут значит
        /// потерять её из-под тестов.
        /// </summary>
        private void RenderTransferList()
        {
            var list = this.FindControl<StackPanel>("TransferList");
            var status = this.FindControl<TextBlock>("TransferStatusLabel");
            var tabs = this.FindControl<Grid>("TransferTabs");
            var search = this.FindControl<TextBox>("TransferSearchBox");
            if (list == null) return;

            var i18n = I18nService.Instance;
            var state = TransferTargetsPresenter.SelectState(
                _transferTargets, _transferLoading, _transferError, _transferForbidden);

            list.Children.Clear();
            SetVisible<Button>("TransferRetryBtn", state == TransferPanelState.Error);

            // 403 и 404 значат «списка не будет» — вкладки и поиск в этом
            // состоянии только мешают ручному вводу.
            var listUsable = state is TransferPanelState.Ready or TransferPanelState.Empty or TransferPanelState.Loading;
            if (tabs != null) tabs.IsVisible = listUsable;
            if (search != null) search.IsVisible = state == TransferPanelState.Ready;

            if (status != null)
            {
                status.Text = state switch
                {
                    TransferPanelState.Loading => i18n.Get("LeadPanelSearching", "Поиск..."),
                    TransferPanelState.Error => i18n.Get("TransferLoadFailed", "Не удалось загрузить список"),
                    TransferPanelState.Empty => i18n.Get("TransferNoTargets", "Некому перевести"),
                    _ => string.Empty,
                };
                status.IsVisible = status.Text.Length > 0;
            }

            if (state != TransferPanelState.Ready) return;

            var query = search?.Text ?? string.Empty;
            if (_transferQueuesTab)
            {
                foreach (var queue in TransferTargetsPresenter.FilterQueues(_transferTargets, query))
                    list.Children.Add(BuildQueueRow(queue));
            }
            else
            {
                var own = App.SipService?.CurrentSettings?.Username;
                foreach (var op in TransferTargetsPresenter.FilterOperators(_transferTargets, query, own))
                    list.Children.Add(BuildOperatorRow(op));
            }
        }

        private Button BuildOperatorRow(Models.TransferOperatorTarget target)
        {
            var button = new Button
            {
                Background = new SolidColorBrush(Color.Parse("#152132")),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = $"{target.FullName}  ·  {target.Extension}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#F8FAFC")),
                },
            };
            button.Click += (_, __) => RequestTransfer(Models.TransferTargetKind.Extension, target.Extension);
            return button;
        }

        private Button BuildQueueRow(Models.TransferQueueTarget target)
        {
            var disabledKey = TransferTargetsPresenter.QueueDisabledKey(target.DisabledReason);
            var enabled = disabledKey == null;
            var i18n = I18nService.Instance;

            var caption = enabled
                ? (string.IsNullOrEmpty(target.Description) ? target.Name : $"{target.Name}  ·  {target.Description}")
                : $"{target.Name}  ·  {i18n.Get(disabledKey!, "")}";

            var button = new Button
            {
                Background = new SolidColorBrush(Color.Parse("#152132")),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                IsEnabled = enabled,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = caption,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse(enabled ? "#F8FAFC" : "#5B6D82")),
                },
            };
            if (enabled)
                button.Click += (_, __) => RequestTransfer(Models.TransferTargetKind.Queue, target.Name);
            return button;
        }

        private void RequestTransfer(Models.TransferTargetKind kind, string value)
        {
            AppLogger.Log("Transfer", $"Transfer requested: {kind} -> {value}");
            OnTransferRequested?.Invoke(this, new Models.TransferRequest(kind, value, CurrentCallerNumber()));
        }
```

- [ ] **Step 6: Перевести событие и существующие вызовы на новый тип**

Объявление события (около строки 1149):

```csharp
        public event EventHandler<Models.TransferRequest>? OnTransferRequested;
```

`TransferToLeadOwner()` (около строки 783) — заменить `OnTransferRequested?.Invoke(this, extension);` на:

```csharp
            OnTransferRequested?.Invoke(this, new Models.TransferRequest(
                Models.TransferTargetKind.Extension, extension, CurrentCallerNumber()));
```

`ConfirmTransfer()` (около строки 1132) — так же, со значением из `TransferNumberBox`.

Добавить приватный хелпер `CurrentCallerNumber()`, возвращающий номер второй стороны — то же поле, из которого вид рисует номер в шапке звонка.

- [ ] **Step 7: Собрать**

Run: `cd /c/work/vv-phone-widget && dotnet build OrbitalSIP/OrbitalSIP.csproj`
Expected: Build succeeded. При ошибках `SolidColorBrush` / `Thickness` / `HorizontalAlignment` дописать `using Avalonia;`, `using Avalonia.Media;`, `using Avalonia.Layout;`.

- [ ] **Step 8: Коммит**

```bash
git add OrbitalSIP/Views/ActiveCallView.axaml.cs
git commit -m "feat(transfer): load, render and pick a transfer target in the call panel"
```

---

### Task 14: маршрутизация в `MainWindow`

**Files:**
- Modify: `OrbitalSIP/MainWindow.axaml.cs:763`

- [ ] **Step 1: Заменить подписку**

```csharp
            callView.OnTransferRequested += async (_, request) => await RouteTransferAsync(request);
```

- [ ] **Step 2: Добавить поле и метод**

```csharp
        private readonly TransferService _transferService = new();

        /// <summary>
        /// Перевод идёт через бэк: он один проверяет, что цель принадлежит
        /// организации, и он один умеет отправить абонента в очередь.
        ///
        /// SIP REFER остаётся запасным путём для ОПЕРАТОРА и только для него:
        /// его экстеншн — настоящий набираемый номер, а имя очереди не является
        /// экстеншном ни в одном контексте, так что REFER на него уходит в
        /// никуда, и молчаливый фолбэк потерял бы звонок.
        /// </summary>
        private async Task RouteTransferAsync(Models.TransferRequest request)
        {
            var i18n = I18nService.Instance;

            var result = await _transferService.TransferAsync(
                request.Kind, request.Value, request.CallerNumber);

            switch (result.Outcome)
            {
                case Models.TransferOutcome.Ok:
                    HttpErrorNotifier.Notify(i18n.Get("TransferDone", "Звонок переведён"));
                    return;

                case Models.TransferOutcome.ChannelUnresolved
                    when request.Kind == Models.TransferTargetKind.Extension:
                    AppLogger.Log("Transfer", "Backend could not resolve the channel; falling back to SIP REFER.");
                    if (App.SipService != null)
                        await App.SipService.BlindTransferAsync(request.Value);
                    return;

                default:
                    HttpErrorNotifier.Notify(
                        i18n.Get("TransferFailed", "Не удалось перевести звонок")
                        + (string.IsNullOrEmpty(result.Error) ? "" : $": {result.Error}"));
                    return;
            }
        }
```

- [ ] **Step 3: Собрать и прогнать тесты**

Run: `cd /c/work/vv-phone-widget && dotnet build OrbitalSIP/OrbitalSIP.csproj && dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
Expected: Build succeeded, PASS.

- [ ] **Step 4: Коммит**

```bash
git add OrbitalSIP/MainWindow.axaml.cs
git commit -m "feat(transfer): route a transfer through the backend, SIP REFER as the fallback"
```

---

### Task 15: ручная проверка на живом стенде

**Files:** нет — проверка.

- [ ] **Step 1: Запустить виджет против стенда с задеплоенными слайсами 1–3**

Run: `cd /c/work/vv-phone-widget && dotnet run --project OrbitalSIP/OrbitalSIP.csproj`

- [ ] **Step 2: Принять звонок, открыть панель перевода**

Expected: вкладка «Операторы» открыта, в списке только свободные зарегистрированные коллеги, себя в списке нет.

- [ ] **Step 3: Перевести на оператора**

Expected: коллеге приходит звонок, у переводящего звонок завершается, в логе `Transfer requested: Extension -> …`, тост «Звонок переведён».

- [ ] **Step 4: Перевести на очередь**

Expected: вкладка «Очереди» показывает очереди организации; после выбора абонент попадает в очередь.
Проверить: `psql "$DATABASE_URL" -c "SELECT time, queuename, event FROM queue_log ORDER BY time DESC LIMIT 5"` — свежая запись входа.

- [ ] **Step 5: Проверить деградацию**

Остановить бэк, открыть панель.
Expected: вкладок и списка нет, поле ручного ввода на месте, ввод номера коллеги переводит звонок через SIP REFER.

- [ ] **Step 6: Проверить изоляцию организаций**

```bash
curl -s -X POST -H "Authorization: Bearer $OTHER_ORG_TOKEN" -H 'Content-Type: application/json' -d '{"channelId":"1719990000.42","target":"sales","targetKind":"queue"}' http://localhost:3000/api/calls/transfer
```

Expected: `{"ok":false,"error":"Unknown queue \"sales\""}`.

- [ ] **Step 7: Разбор расхождений**

Если что-то из шагов 2–6 разошлось с ожиданием — починить прежде, чем мержить слайс 5.

**Слайс 5 готов к мержу в main.**

---

## Порядок работ

1. Слайсы 1–3 (`crm_mono`, ветка от `origin/main`) — по PR на слайс, мерж и деплой по мере готовности.
2. Слайс 4 (`vv-phone-widget`) — мержится параллельно бэку, ничего не ждёт.
3. Слайс 5 (`vv-phone-widget`) — после слайса 4; инсталлятор собирать после деплоя слайсов 1–3.

---

## Перенос на `feat/freeswitch-all`

Ветка отстаёт от main на 70 коммитов и подтягивает его периодически. При очередном подтягивании эта фича приедет вместе с остальным main, и мерж-конфликты будут в трёх файлах. Что с ними делать:

| Файл | Действие при мерже |
|---|---|
| `sip-trunk/dialplan-context.util.ts` | На feat-ветке файл уже есть с тем же именем и почти тем же содержимым — конфликт сведётся к одной строке `\| 'queues'` в `DialplanContextKind`. Переэкспорт из `sip-trunk.service.ts` там уже не нужен: проверить, не остался ли он лишним после мержа |
| `calls/services/call-transfer.service.ts` | На feat-ветке файл удалён. Логику `endpointFor` перенести в `AsteriskCallControlDriver.transfer` как выбор `Context`, а в `FreeswitchCallControlDriver.transfer` — как контекст в `uuid_transfer`. Добавить параметр `targetKind` в `TelephonyCallControlDriver` |
| `calls/controllers/calls.controller.ts` | Тело запроса на feat-ветке — `{callId, targetExtension}`. Переименовать поля, оставить `targetKind`, `assertTargetAllowed` и аудит как есть |

Остальное переносится без правок: `queue-transfer.dialplan.ts`, провижининг, миграция, `TransferTargetService`, весь виджет.

**Виджету при этом понадобится правка одного места** — имена полей в теле POST (`channelId`/`target` → `callId`/`targetExtension`). Делать её тогда, когда feat-ветка станет main, не раньше.

**FreeSWITCH:** контекст `org-<uuid>-queues` там обязан появиться в рендеренном конфиге — это деплой (ADR-023 §2), не миграция.
