# OrbitalSIP — Code Review (баги)

Метод: многоагентный обзор. Финдеры по 8 группам файлов → каждая находка проверена вторым скептик-агентом (чтение исходника, презумпция «не баг»). Подтверждено **57 из 95** кандидатов. ~7800 строк C#/Avalonia.

Severity: 🔴 critical · 🟠 high · 🟡 medium · ⚪ low

---

## 🔴 SYSTEMIC — TLS-валидация отключена во всём приложении

`ServerCertificateCustomValidationCallback = (_,_,_,_) => true` — принимается **любой** сертификат (просрочен, самоподписан, чужой хост, MITM). Дублируется в **8 файлах**, все шлют Bearer-токен/учётки. Активный MITM на сети крадёт JWT, SIP-пароль, PII лидов/звонков, подменяет ответы. Уже отмечено в `plan.md:151`.

| Файл | Строки | Что течёт |
|------|--------|-----------|
| [LoginView.axaml.cs:20](OrbitalSIP/Views/LoginView.axaml.cs:20) | 20-23 | логин/пароль + SIP-пароль (🔴 critical — точка входа учёток) |
| [LeadService.cs:15](OrbitalSIP/Services/LeadService.cs:15) | 15-19 | JWT + данные лидов |
| [CallInfoService.cs:21](OrbitalSIP/Services/CallInfoService.cs:21) | 21-25 | JWT + PII звонящего (телефоны) |
| [FlowsService.cs:17](OrbitalSIP/Services/FlowsService.cs:17) | 17-21 | JWT + данные сценариев |
| [ScriptService.cs:17](OrbitalSIP/Services/ScriptService.cs:17) | 17-21 | JWT + CDR/скрипты |
| [StatusService.cs:27](OrbitalSIP/Services/StatusService.cs:27) | 27-30 | JWT + presence |
| [OperatorStatsControl.axaml.cs:22](OrbitalSIP/Views/OperatorStatsControl.axaml.cs:22) | 22-26 | JWT + статистика |
| [RecentsView.axaml.cs:32](OrbitalSIP/Views/RecentsView.axaml.cs:32) | 32 | JWT + CDR |

**Фикс:** убрать callback везде — дефолтная проверка по системному хранилищу. Если нужен внутренний CA — пиннинг конкретного thumbprint для известного хоста, никогда `=> true`. Завести один общий `HttpClient`/handler-фабрику, чтобы не дублировать.

---

## 🟠 HIGH

### 1. Гонка в `OnCallEnded` — двойной cleanup, NRE/повреждение состояния
[SipService.cs:648](OrbitalSIP/Services/SipService.cs:648) (648-659, 661-670). Guard `if (State==Idle) return` держит `_lock` только на **чтение**, потом отпускает и зовёт `CleanupMedia()`/`SetState()` вне лока. `OnCallEnded` прилетает с нескольких потоков SIPSorcery (`OnCallHungup`, `OnRtpClosed`, BYE). Два потока проходят guard, оба входят в `CleanupMedia` (тоже без синхронизации): один обнуляет `_mediaSession`, другой зовёт `.Close()` на null/disposed → double-Close/NRE. `State` — обычное авто-свойство, без `volatile`, запись Idle вне лока не видна другим потокам.
**Фикс:** атомарно — `lock(_lock){ if(State==Idle) return; State=Idle; }`, затем `CleanupMedia`/события вне лока; поля медиа обнулять под локом до dispose.

### 2. `ShutdownApp` → `Environment.Exit(0)` минует всю очистку
[MainWindow.axaml.cs:678](OrbitalSIP/MainWindow.axaml.cs:678). `Environment.Exit(0)` не поднимает событие `desktop.Exit` ([App.axaml.cs:53](OrbitalSIP/App.axaml.cs:53)) — единственное место, где зовётся `SipService.Dispose()` (`_reg.Stop()`, hangup, transport shutdown). Итог: после выхода оператор **остаётся REGISTERED** на PBX (фантомная регистрация, звонки звонят в мёртвый клиент), активный звонок не получает BYE. Все 4 пути выхода (Recents/Settings/ActiveCall/Dialer) идут сюда.
**Фикс:** `((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown();` — как уже сделано в `MenuExit_Click` ([App.axaml.cs:96](OrbitalSIP/App.axaml.cs:96)).

### 3. Сохранение настроек во время звонка роняет звонок
[MainWindow.axaml.cs:393](OrbitalSIP/MainWindow.axaml.cs:393) (393-411). `OnSaveRequested` → `SipService.Start(settings)` без проверки состояния. `Start()` пересоздаёт `_transport`, но **не** зовёт `Hangup()`/`CleanupMedia()` на текущем `_activeCall`. Старый диалог остаётся на уже-disposed транспорте → BYE не уходит (удалённая сторона висит до RTP-таймаута), аудио-endpoint утекает, `State` застревает Active. Кнопка Settings доступна в активном звонке ([MainWindow.axaml.cs:563](OrbitalSIP/MainWindow.axaml.cs:563)).
**Фикс:** в `Start()` при `State != Idle` сначала `Hangup()`/`CleanupMedia()`; или блокировать Save во время звонка.

### 4. Баннер верификации мигает и исчезает
[SurveyDialog.axaml.cs:428](OrbitalSIP/Views/SurveyDialog.axaml.cs:428) (428-458). При успешном ответе один `Dispatcher.Post` показывает `VerificationBanner` (mismatch/нет данных), следом другой `Post` зовёт `RenderNode`, а тот на [:285](OrbitalSIP/Views/SurveyDialog.axaml.cs:285) безусловно `Show("VerificationBanner", false)`. Обе задачи в одном проходе диспетчера (FIFO) → баннер показан и скрыт до отрисовки. Оператор **никогда не видит** предупреждение о расхождении данных на не-терминальных узлах.
**Фикс:** переносить состояние верификации в `RenderNode` следующего узла, либо не скрывать баннер безусловно.

### 5+6. `DispatcherTimer` 1 Гц никогда не останавливается
[ActiveCallView.axaml.cs:50](OrbitalSIP/Views/ActiveCallView.axaml.cs:50) (50-57, stop только на 106/360) и [ActiveCallWidgetView.axaml.cs:49](OrbitalSIP/Views/ActiveCallWidgetView.axaml.cs:49) (вообще нет `Stop()`). При minimize/expand/transfer создаётся новый view+timer, старый таймер рутится в очереди диспетчера → старый view не собирается GC, `OnTick` тикает на отсоединённом дереве вечно. Утечка на каждый цикл minimize/expand и каждый hangup (виджет). Накапливается на весь сеанс.
**Фикс:** `OnDetachedFromVisualTree` → `_timer?.Stop(); _timer=null;` (или MainWindow останавливает таймер уходящего view перед свопом контента).

---

## 🟡 MEDIUM

| # | Файл:строка | Проблема | Фикс |
|---|-------------|----------|------|
| 7 | [SipService.cs:182](OrbitalSIP/Services/SipService.cs:182) | `CallAsync` ставит `_activeCall`/`State` **вне** `_lock` — гонка со входящим INVITE и реентрантностью | весь переход под `_lock` |
| 8 | [SipService.cs:259](OrbitalSIP/Services/SipService.cs:259) | BYE-хендлер зовёт `OnCallEnded` без проверки, что BYE относится к активному диалогу — чужой/устаревший BYE рвёт звонок | сверять Call-ID диалога |
| 9 | [SipService.cs:324](OrbitalSIP/Services/SipService.cs:324) | `AnswerAsync` при сбое аудио обнуляет `_activeCall`, но `_pendingUas` уже очищен и UAS не reject'нут серверу | reject UAS перед сбросом |
| 10 | [SipService.cs:690](OrbitalSIP/Services/SipService.cs:690) | `Log` = `File.AppendAllText` на каждое событие без try/catch — IO-исключение на потоке SIPSorcery валит обработчик | обернуть в try/catch, буферизация |
| 11 | [SipService.cs:493](OrbitalSIP/Services/SipService.cs:493) | `ToggleHold` меняет `IsOnHold`/`State` без лока; `SetState` шлёт события на потоке вызывающего | под `_lock`, маршалинг событий |
| 12 | [SipService.cs:483](OrbitalSIP/Services/SipService.cs:483) | `IsMuted`/`IsOnHold` не сбрасываются в false при новом звонке — mute/hold протекает между звонками | сбрасывать в `CleanupMedia`/старте |
| 13 | [SipService.cs:66](OrbitalSIP/Services/SipService.cs:66) | `Start()` рвёт/заменяет `_transport` без `_lock`, пока `OnSIPRequest`/коллбэки используют его на потоках SIPSorcery | синхронизация доступа к `_transport` |
| 14 | [SurveyDialog.axaml.cs:410](OrbitalSIP/Views/SurveyDialog.axaml.cs:410) | Любой сбой `AnswerAsync` трактуется как 409 — ответ оператора молча теряется, без фидбэка | различать ошибки, показывать сбой |
| 15 | [MainWindow.axaml.cs:494](OrbitalSIP/MainWindow.axaml.cs:494) | Авто-открытый модальный опрос не закрывается, когда звонок завершился под ним | закрывать диалог по событию конца звонка |
| 16 | [UpdateService.cs:127](OrbitalSIP/Services/UpdateService.cs:127) | Guard «идёт звонок» проверяется 1 раз, до 20-мин загрузки; перед `Process.Start` (installer `/CLOSEAPPLICATIONS`) не перепроверяется → убивает активный звонок | перепроверить `State==Idle` перед стартом инсталлятора |
| 17 | [StatusService.cs:127](OrbitalSIP/Services/StatusService.cs:127) | `durationMinutes` не уходит на сервер — при перезапуске/на другом устройстве оператор навсегда «на паузе» | слать длительность в payload, таймер от серверного состояния |
| 18 | [GlobalHotkeyService.cs:146](OrbitalSIP/Services/GlobalHotkeyService.cs:146) | Колбэк LL-хука диспатчит асинхронно — ломает подавление хоткея и тормозит глобальный хук | обрабатывать синхронно в колбэке |
| 19 | [AppLogger.cs:21](OrbitalSIP/Services/AppLogger.cs:21) | Безграничный рост лога + синхронный `File.AppendAllText` на каждый вызов | ротация, async/очередь |
| 20 | [ActiveCallView.axaml.cs:29](OrbitalSIP/Views/ActiveCallView.axaml.cs:29) | Конструктор не синхронит `_onHold/_muted` с `SipService` — после expand/transfer первое нажатие Mute/Hold инвертировано | передавать `isMuted/isOnHold` в конструктор (как виджет) |
| 21 | [SettingsView.axaml.cs:26](OrbitalSIP/Views/SettingsView.axaml.cs:26) | Подписка на статические события синглтонов без отписки — утечка + мульти-инвок на мёртвых view | `OnDetachedFromVisualTree` → отписка (как в `WidgetView`) |
| 22 | [LoginView.axaml.cs:104](OrbitalSIP/Views/LoginView.axaml.cs:104) | Тело HTTP-ошибки (может нести токены) логируется и показывается в баннере | логировать только код + санитизированное сообщение |
| 23 | [LoginView.axaml.cs:45](OrbitalSIP/Views/LoginView.axaml.cs:45) | `async void` KeyDown: Enter во время идущего логина запускает второй `AttemptLogin` → двойной `SipService.Start` | флаг `_loggingIn`, ранний выход |
| 24 | [RecentsView.axaml.cs:89](OrbitalSIP/Views/RecentsView.axaml.cs:89) | Диапазон дат CDR берёт сегодняшнюю **UTC**-дату как локальный рабочий день — пропуск/лишние звонки у краёв суток | локальная дата или серверный TZ |

---

## ⚪ LOW

| # | Файл:строка | Проблема |
|---|-------------|----------|
| 25 | [SoundService.cs:42](OrbitalSIP/Services/SoundService.cs:42) | `_prevState` читается/пишется без синхронизации с фоновых потоков |
| 26 | [SipService.cs:200](OrbitalSIP/Services/SipService.cs:200) | Два `OnCallHungup` оба гонят `OnCallEnded`; защита только `State==Idle` |
| 27 | [SipSettings.cs:59](OrbitalSIP/Services/SipSettings.cs:59) | `Save()` не атомарен, без обработки ошибок — краш при записи рушит файл настроек |
| 28 | [SoundService.cs:52](OrbitalSIP/Services/SoundService.cs:52) | Переходы OnHold и IncomingRinging→Active без звука; OnHold оставляет луп играть |
| 29 | [SipService.cs:118](OrbitalSIP/Services/SipService.cs:118) | ContactHost-проба только при literal-IP; для hostname-серверов ContactHost не задаётся |
| 30 | [MainWindow.axaml.cs:458](OrbitalSIP/MainWindow.axaml.cs:458) | Двойной `StartAnimation` при ответе на входящий в Panel-режиме (мёртвый код/churn) |
| 31 | [Program.cs:133](OrbitalSIP/Program.cs:133) | Pipe-сервер: номер `tel:`, пришедший до подписки MainWindow, молча теряется |
| 32 | [MainWindow.axaml.cs:62](OrbitalSIP/MainWindow.axaml.cs:62) | Closing отменяет закрытие и прячет окно → `OnClosed`-очистка в норме не выполняется |
| 33 | [SurveyDialog.axaml.cs:623](OrbitalSIP/Views/SurveyDialog.axaml.cs:623) | Next на числовом узле шлёт сырую строку без числовой/культурной валидации |
| 34 | [LoggedCallService.cs:118](OrbitalSIP/Services/LoggedCallService.cs:118) | `Save()` неатомарный overwrite — краш при записи рушит `logged-calls.json` |
| 35 | [UpdateService.cs:100](OrbitalSIP/Services/UpdateService.cs:100) | Парсинг версии срезает только ведущий `v`, не тянет `-beta`/`+build` (SemVer) |
| 36 | [JwtDecoder.cs:44](OrbitalSIP/Services/JwtDecoder.cs:44) | Декод base64url молча возвращает null при битом токене, без лога — оператор- id тихо деградирует |
| 37 | [StatusService.cs:54](OrbitalSIP/Services/StatusService.cs:54) | Отсчёт паузы на `DateTime.Now` — DST/перевод часов даёт ±час/застрявшую паузу |
| 38 | [GlobalHotkeyService.cs:90](OrbitalSIP/Services/GlobalHotkeyService.cs:90) | Сбой установки хука молча игнорируется — мёртвые хоткеи без диагностики |
| 39 | [GlobalHotkeyService.cs:130](OrbitalSIP/Services/GlobalHotkeyService.cs:130) | Не-латинские одиночные хоткеи мапятся в неверный VK |
| 40 | [FlowsService.cs:59](OrbitalSIP/Services/FlowsService.cs:59) | Сырые тела HTTP-ошибок логируются/показываются (утечка токен/PII) |
| 41 | [AppLogger.cs:23](OrbitalSIP/Services/AppLogger.cs:23) | Таймстамп форматируется текущей культурой |
| 42 | [StatusState.cs:24](OrbitalSIP/Models/StatusState.cs:24) | Compat-свойство Paused считает пустой manualStatus за «на паузе» |
| 43 | [ActiveCallView.axaml.cs:70](OrbitalSIP/Views/ActiveCallView.axaml.cs:70) | Раздельные метки мин/сек переполняются при ≥100 минут |
| 44 | [ActiveCallView.CallInfo.cs:168](OrbitalSIP/Views/ActiveCallView.CallInfo.cs:168) | Async-обработчик копирования в строке гонит состояние иконки, держит ссылку на view |
| 45 | [SettingsView.axaml.cs:127](OrbitalSIP/Views/SettingsView.axaml.cs:127) | GotFocus затирает сохранённый хоткей плейсхолдером, теряя его при клике мимо |
| 46 | [SettingsView.axaml.cs:277](OrbitalSIP/Views/SettingsView.axaml.cs:277) | Port сохраняется как сырой текст без числовой валидации |
| 47 | [ExpandedView.axaml.cs:30](OrbitalSIP/Views/ExpandedView.axaml.cs:30) | Дублированный null-check (copy-paste) на проводке bottomNav |
| 48 | [ExpandedView.axaml.cs:112](OrbitalSIP/Views/ExpandedView.axaml.cs:112) | Async-лямбда Copy реентрит за 1200мс restore, портит `originalKind` |
| 49 | [StatusPopupControl.axaml.cs:217](OrbitalSIP/Views/StatusPopupControl.axaml.cs:217) | Отсчёт паузы через Minutes/Seconds даёт неверное/отрицательное время через границу суток |
| 50 | [CdrItemViewModel.cs:38](OrbitalSIP/ViewModels/CdrItemViewModel.cs:38) | `ToLocalTime()` зависит от `DateTimeKind` десериализации — двойной/нулевой сдвиг |
| 51 | [CdrItemViewModel.cs:55](OrbitalSIP/ViewModels/CdrItemViewModel.cs:55) | `DisplayStatus` показывает сырую disposition, минуя i18n, светит бэкенд-коды |

(+ #52-57 — менее значимые культурно-парсинговые/UX-варианты тех же классов; см. полный JSON воркфлоу.)

---

## Приоритет
1. **TLS** — один фикс закрывает 8 находок и весь класс утечки учёток. Делать первым.
2. **SIP-жизненный цикл** (#1,2,3,7,8,9,13) — гонки и утечки звонков, прямой пользовательский урон (фантомная регистрация, оборванные звонки).
3. **Утечки таймеров/подписок** (#5,6,21) — деградация за смену.
4. Остальное по severity.

Самый частый системный антипаттерн: разделяемое состояние (`SipService`, культурные даты, event-подписки) трогается с нескольких потоков/без отписки. Стоит ввести единый паттерн локинга в `SipService` и `OnDetachedFromVisualTree`-отписку во всех view.
