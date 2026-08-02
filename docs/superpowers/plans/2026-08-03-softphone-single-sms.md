# Single-Call SMS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan.

**Goal:** Добавить в софтфон отправку одного SMS по шаблону или свободным текстом только абоненту текущего разговора либо выбранного звонка из истории.

**Architecture:** Клиент передаёт неизменяемый источник звонка (`active` с Asterisk `linkedid` либо `history` с UUID CDR), но никогда не передаёт номер телефона. Новый узкий CRM endpoint проверяет право оператора, сам определяет адресата по звонку, проверяет шаблон и запреты на отправку, сохраняет идемпотентное `SmsMessage` и публикует существующую RabbitMQ-задачу. Avalonia-клиент показывает компактный диалог с заблокированным адресатом, выбором шаблона/свободного текста и явным подтверждением.

**Tech Stack:** NestJS, TypeORM/PostgreSQL, RabbitMQ, Vitest, Avalonia/.NET 8, xUnit.

## Global Constraints

- Работать в отдельных ветках/worktree обоих репозиториев; не затрагивать пользовательские незакоммиченные файлы.
- Для каждого поведения сначала получить RED-тест, затем внести минимальную реализацию и увидеть GREEN.
- Не вызывать SMS-провайдера напрямую: использовать `QueueProducerService.queueSms`.
- Не добавлять телефон в DTO запроса. Бэк принимает только `requestId`, `source`, `content`, `templateId`.
- Не давать операторам широкое право `sms:send`; добавить `sms:send:own-call`.
- Не отправлять реальное SMS ни в тестах, ни во время smoke-проверки.
- Текст успеха означает «поставлено в очередь», а не «доставлено».

---

### Task 1: Add the narrow CRM permission

**Files:**
- Modify: `C:\work\crm_mono\apps\back\src\app\core\rbac\rbac.catalog.ts`
- Create: `C:\work\crm_mono\apps\back\src\migrations\1791400000000-AddOwnCallSmsPermission.ts`
- Modify: `C:\work\crm_mono\apps\back\src\migrations\index.ts`
- Test: `C:\work\crm_mono\apps\back\src\app\core\rbac\rbac.catalog.vitest.ts`

**Interfaces and invariants:**
- Permission key: `sms:send:own-call`.
- Grant to `admin`, `owner`, `cc_manager`, `cc_operator`, `tm_manager`, `tm_operator`.
- Do not grant `sms:send` as a consequence.
- Migration inserts permission and role mappings idempotently; `down` deletes only this key.

**Step 1: Write the failing catalog tests**

Assert every allowed role has `sms:send:own-call`, an unrelated role does not, and `cc_operator`/`tm_operator` do not acquire `sms:send`.

**Step 2: Run RED**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/core/rbac/rbac.catalog.vitest.ts
```

Expected: failure because the new permission is absent.

**Step 3: Implement the catalog and migration**

Add the permission definition, role assignments, migration export, and SQL scoped to the new key.

**Step 4: Run GREEN and build**

Repeat the focused test, then run `npm run build:back`.

**Step 5: Commit**

```powershell
git add apps/back/src/app/core/rbac/rbac.catalog.ts apps/back/src/app/core/rbac/rbac.catalog.vitest.ts apps/back/src/migrations/1791400000000-AddOwnCallSmsPermission.ts apps/back/src/migrations/index.ts
git commit -m "feat(rbac): add own-call SMS permission"
```

---

### Task 2: Resolve an authorized call source to a recipient

**Files:**
- Create: `C:\work\crm_mono\apps\back\src\app\modules\messages\services\call-recipient-resolver.service.ts`
- Test: `C:\work\crm_mono\apps\back\src\app\modules\messages\services\call-recipient-resolver.service.vitest.ts`
- Modify: `C:\work\crm_mono\apps\back\src\app\modules\messages\messages.module.ts`

**Interfaces:**

```ts
export type SmsCallSource =
  | { type: 'active'; id: string }
  | { type: 'history'; id: string };

export interface ResolvedCallRecipient {
  phoneNumber: string;
  callUniqueId: string;
  contactId?: string;
  leadId?: string;
}

resolve(source: SmsCallSource, user: CurrentUserPayload): Promise<ResolvedCallRecipient>
```

**Step 1: Write RED history tests**

Cover own CDR resolution, another operator, another organization, invalid/internal number, and contact/lead propagation. Reuse operator matching from `CdrService.findAll`: `CallSummary.agent`, `channel`, `dstchannel`, `accountcode`, `src`, `dst`. Identify history by `Cdr.id`, never by display number or `uniqueid`.

**Step 2: Run RED**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/messages/services/call-recipient-resolver.service.vitest.ts
```

Expected: missing resolver.

**Step 3: Implement history resolution**

Query `Cdr` joined with `CallSummary`/`CallLog`, enforce organization and authenticated operator ownership, derive the external side by direction, normalize with the shared phone utility, and reject missing/ambiguous/invalid recipients.

**Step 4: Add RED active-call tests**

Mock `AriService.getChannels()` and `getChannelVariable()`. Cover a matching authenticated-operator channel and external party, plus forged linkedid, missing own channel, ambiguous parties, and invalid number.

**Step 5: Implement active resolution**

Find only `PJSIP/<operator>-...` channels of the authenticated operator, read primary `CHANNEL(linkedid)` with channel-ID fallback, require equality with `source.id`, then derive the external party from channels sharing that linkedid.

**Step 6: Wire without a module cycle**

Import `AriModule` and register required TypeORM entities in `MessagesModule`; do not import `CdrModule`, because it reaches `MessagesModule` through pipeline dependencies.

**Step 7: Run GREEN, build, commit**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/messages/services/call-recipient-resolver.service.vitest.ts
npm run build:back
git add apps/back/src/app/modules/messages/services/call-recipient-resolver.service.ts apps/back/src/app/modules/messages/services/call-recipient-resolver.service.vitest.ts apps/back/src/app/modules/messages/messages.module.ts
git commit -m "feat(messages): resolve SMS recipient from owned call"
```

---

### Task 3: Add the idempotent single-call SMS endpoint

**Files:**
- Create: `C:\work\crm_mono\apps\back\src\app\modules\messages\dto\send-call-sms.dto.ts`
- Create: `C:\work\crm_mono\apps\back\src\app\modules\messages\controllers\sms-compose.controller.ts`
- Create: `C:\work\crm_mono\apps\back\src\app\modules\messages\services\sms-compose.service.ts`
- Test: `C:\work\crm_mono\apps\back\src\app\modules\messages\controllers\sms-compose.controller.vitest.ts`
- Test: `C:\work\crm_mono\apps\back\src\app\modules\messages\services\sms-compose.service.vitest.ts`
- Modify: `C:\work\crm_mono\apps\back\src\app\modules\messages\entities\sms-message.entity.ts`
- Modify: `C:\work\crm_mono\apps\back\src\app\modules\messages\messages.module.ts`

**HTTP contract:**

```http
POST /api/messages/sms/send-from-call
{
  "requestId": "uuid",
  "source": { "type": "active|history", "id": "opaque-call-id" },
  "content": "final operator-confirmed text",
  "templateId": "uuid-or-null"
}
```

Response: `{ "messageId": "same-request-uuid", "status": "queued" }`.

**Step 1: Write RED DTO/controller tests**

Assert strict validation (`whitelist`, `forbidNonWhitelisted`, `transform`), UUID request, discriminated source, bounded nonblank content, nullable UUID template, rejection of extra `phoneNumber`, `AbilityGuard`, `@CheckAbility('sms:send:own-call')`, and current-user forwarding.

**Step 2: Run RED**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/messages/controllers/sms-compose.controller.vitest.ts
```

**Step 3: Implement DTO/controller minimally**

Use a controller-local strict `ValidationPipe` and delegate to `SmsComposeService`.

**Step 4: Write RED service tests**

Cover resolver-only phone source; active org-scoped SMS template; rejection of inactive/foreign/non-SMS templates; DNC/contact blacklist before persistence; explicit `SmsMessage.id=requestId`; `PENDING` state; audit metadata `createdByUserId`, `operatorId`, `sourceType`, `sourceId`, `callUniqueId`; exactly one `queueSms` call with `campaignId: ''`; same-owner idempotent retry; cross-owner duplicate rejection; and `FAILED` state on broker failure. A successful API response is `queued`, while DB remains `PENDING` for the consumer's claim transition.

**Step 5: Run RED**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/messages/services/sms-compose.service.vitest.ts
```

**Step 6: Implement service and module wiring**

Resolve recipient, validate template/content, check DNC/blacklist, insert with explicit UUID, publish through `QueueProducerService.queueSms`, and update only to `FAILED` on publish failure. On duplicate-key race re-read and enforce the same org/operator ownership.

**Step 7: Run GREEN, build, commit**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/modules/messages/controllers/sms-compose.controller.vitest.ts apps/back/src/app/modules/messages/services/sms-compose.service.vitest.ts
npm run build:back
git add apps/back/src/app/modules/messages
git commit -m "feat(messages): queue SMS from owned call"
```

---

### Task 4: Add typed SMS transport to Avalonia

**Files:**
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Models\SmsModels.cs`
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Services\SmsService.cs`
- Test: `C:\work\vv-phone-widget\OrbitalSIP.Tests\SmsModelsTests.cs`
- Test: `C:\work\vv-phone-widget\OrbitalSIP.Tests\SmsServiceTests.cs`
- Modify: `C:\work\vv-phone-widget\OrbitalSIP\App.axaml.cs`

**C# contract:**

```csharp
public sealed record SmsCallSource(string Type, string Id);
public sealed record SendCallSmsRequest(Guid RequestId, SmsCallSource Source, string Content, Guid? TemplateId);
public sealed record SendCallSmsResult(Guid MessageId, string Status);

Task<IReadOnlyList<MessageTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default);
Task<SendCallSmsResult> SendFromCallAsync(SendCallSmsRequest request, CancellationToken cancellationToken = default);
```

**Step 1: Write RED transport tests**

Assert active/history JSON, absence of phone, templates URL `/api/messages/templates?channel=sms&isActive=true&page=1&limit=100`, send URL, bearer token, cancellation, and API error extraction.

**Step 2: Run RED**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~SmsModelsTests|FullyQualifiedName~SmsServiceTests"
```

**Step 3: Implement and wire**

Inject `HttpClient` or a test handler, preserve normal TLS validation, follow existing auth/base-address conventions, deserialize paginated templates/send result, register `App.SmsService`, and dispose it with app services.

**Step 4: Run GREEN and commit**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter "FullyQualifiedName~SmsModelsTests|FullyQualifiedName~SmsServiceTests"
git add OrbitalSIP/Models/SmsModels.cs OrbitalSIP/Services/SmsService.cs OrbitalSIP.Tests/SmsModelsTests.cs OrbitalSIP.Tests/SmsServiceTests.cs OrbitalSIP/App.axaml.cs
git commit -m "feat(sms): add call SMS API client"
```

---

### Task 5: Build the locked-recipient compose dialog

**Files:**
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Models\SmsComposeState.cs`
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Views\SmsComposeDialog.axaml`
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Views\SmsComposeDialog.axaml.cs`
- Test: `C:\work\vv-phone-widget\OrbitalSIP.Tests\SmsComposeStateTests.cs`
- Modify: `C:\work\vv-phone-widget\OrbitalSIP\Localization\Strings.{ru,kk,tg,uz}.axaml`

**State contract:**
- Constructor receives source and display-only locked recipient.
- Modes: Template and FreeText.
- Template selection copies text into the final editable textarea.
- Send disabled for blank/over-limit content, missing template in template mode, or in-flight request.
- First Send opens inline confirmation with recipient/final text; Confirm calls API.
- One request UUID is reused on retry until success or content/source changes.

**Step 1: Write RED state tests**

Cover locked source/recipient, mode switch, template copy and subsequent edit, validation, character count, confirmation, double-click guard, stable retry ID, and regenerated ID after edit.

**Step 2: Run RED**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~SmsComposeStateTests
```

**Step 3: Implement state and dialog**

Keep request construction UI-independent. Build a compact dark modal using existing resources: locked recipient header, mode toggle, template selector, editable final textarea, count, progress/error, Cancel/Send, inline confirmation. Success copy: «SMS поставлено в очередь».

**Step 4: Add all four localization dictionaries**

Use the identical key set for labels, validation, confirmation, queued success, and fallback errors.

**Step 5: Test, build, commit**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~SmsComposeStateTests
dotnet build vv-phone-widget.sln
git add OrbitalSIP/Models/SmsComposeState.cs OrbitalSIP/Views/SmsComposeDialog.axaml OrbitalSIP/Views/SmsComposeDialog.axaml.cs OrbitalSIP.Tests/SmsComposeStateTests.cs OrbitalSIP/Localization
git commit -m "feat(sms): add locked-recipient compose dialog"
```

---

### Task 6: Open compose from the active call

**Files:**
- Modify: `C:\work\vv-phone-widget\OrbitalSIP\Views\ActiveCallView.axaml`
- Modify: `C:\work\vv-phone-widget\OrbitalSIP\Views\ActiveCallView.axaml.cs`
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Models\ActiveCallSmsContext.cs`
- Test: `C:\work\vv-phone-widget\OrbitalSIP.Tests\ActiveCallSmsContextTests.cs`

**Step 1: Write RED context tests**

Assert source `{ Type = "active", Id = primaryLinkedId }`, no display number in API request, and missing linkedid blocks compose.

**Step 2: Run RED**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~ActiveCallSmsContextTests
```

**Step 3: Integrate**

Add the SMS action to the grid. On click, obtain primary linkedid via existing channel lookup, build the active source, and open the dialog with locked display number. Show localized error if linkedid cannot be verified.

**Step 4: GREEN, build, commit**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~ActiveCallSmsContextTests
dotnet build vv-phone-widget.sln
git add OrbitalSIP/Views/ActiveCallView.axaml OrbitalSIP/Views/ActiveCallView.axaml.cs OrbitalSIP/Models/ActiveCallSmsContext.cs OrbitalSIP.Tests/ActiveCallSmsContextTests.cs
git commit -m "feat(sms): compose from active call"
```

---

### Task 7: Open compose from call history

**Files:**
- Modify: `C:\work\vv-phone-widget\OrbitalSIP\Views\RecentsView.axaml`
- Modify: `C:\work\vv-phone-widget\OrbitalSIP\Views\RecentsView.axaml.cs`
- Create: `C:\work\vv-phone-widget\OrbitalSIP\Models\HistoryCallSmsContext.cs`
- Test: `C:\work\vv-phone-widget\OrbitalSIP.Tests\HistoryCallSmsContextTests.cs`

**Step 1: Write RED context tests**

Assert source ID is exactly `CdrEntry.Id`, not `UniqueId`, `DisplayNumber`, `Src`, or `Dst`; display number is UI-only; missing CDR UUID blocks compose.

**Step 2: Run RED**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~HistoryCallSmsContextTests
```

**Step 3: Integrate**

Add SMS beside call/copy/script, bind the row VM through `Tag`, build `SmsCallSource("history", vm.Entry.Id)`, and open the shared dialog with `vm.DisplayNumber` locked.

**Step 4: GREEN, build, commit**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj --filter FullyQualifiedName~HistoryCallSmsContextTests
dotnet build vv-phone-widget.sln
git add OrbitalSIP/Views/RecentsView.axaml OrbitalSIP/Views/RecentsView.axaml.cs OrbitalSIP/Models/HistoryCallSmsContext.cs OrbitalSIP.Tests/HistoryCallSmsContextTests.cs
git commit -m "feat(sms): compose from call history"
```

---

### Task 8: Cross-repository verification and adversarial review

**Step 1: CRM verification**

```powershell
npx vitest run --config apps/back/vitest.config.mts apps/back/src/app/core/rbac/rbac.catalog.vitest.ts apps/back/src/app/modules/messages/services/call-recipient-resolver.service.vitest.ts apps/back/src/app/modules/messages/controllers/sms-compose.controller.vitest.ts apps/back/src/app/modules/messages/services/sms-compose.service.vitest.ts
npm run build:back
```

**Step 2: Widget verification**

```powershell
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
dotnet build vv-phone-widget.sln
```

**Step 3: Static contract checks**

```powershell
rg -n "phone(Number)?" OrbitalSIP/Models/SmsModels.cs OrbitalSIP/Services/SmsService.cs OrbitalSIP/Models/*SmsContext.cs
rg -n "send-from-call|sms:send:own-call|queueSms" C:\work\crm_mono\apps\back\src\app\modules\messages C:\work\crm_mono\apps\back\src\app\core\rbac
```

Expected: no request/context serializes a phone, only narrow permission guards the endpoint, and the provider is reached only through the queue producer.

**Step 4: Adversarial review**

Review IDOR across organizations/operators, linkedid spoofing, ambiguous recipient selection, duplicate-publish races, template tenant leaks, blacklist/DNC bypass, broad RBAC grants, double-click sends, and queued-vs-delivered wording. Fix confirmed issues only after a reproducing RED test.

**Step 5: Final evidence**

Run `git diff --check` and inspect `git status --short` in both repositories. Do not call the live endpoint/provider. Report unit/build evidence separately from the explicitly unperformed real-SMS smoke test.

**Step 6: Commit review fixes if any**

Stage the exact files changed by confirmed review findings, rerun their focused tests, and commit them as `fix(sms): harden own-call SMS flow`. If review finds no defect, create no empty commit.
