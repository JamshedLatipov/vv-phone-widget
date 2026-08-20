# OrbitalSIP — Code Review

**Обзор от 10 августа 2026.** Заменяет обзор от 27 июня: его находки закрыты, кроме двух архитектурных и одной, требующей изменений вне этого репозитория. Одна была неверно оценена — см. «Транспорт».

Кодовая база: ~13.5k строк C#/Avalonia, 81 файл. Тесты: **407/407 зелёные** (было 329).

Severity: 🔴 critical · 🟠 high · 🟡 medium · ⚪ low

---

## Закрыто

### Безопасность

| Что | Где | Суть фикса |
|-----|-----|------------|
| 🔴 Инъекция SIP URI через named pipe | [Program.cs:153](OrbitalSIP/Program.cs:153) | Строка из pipe прогоняется через `NormalizeNumber`, как путь через командную строку. Раньше значение с `@` уходило в `SipService` как готовый SIP URI — INVITE на произвольный хост |
| 🟠 Нет обработки 401 / refresh-токена | [BackendAuth.cs](OrbitalSIP/Services/BackendAuth.cs), [AuthRefreshHandler.cs](OrbitalSIP/Services/AuthRefreshHandler.cs) | Проактивное обновление по claim `exp` в `DelegatingHandler` общего пула, single-flight через семафор, ротация refresh-токена. Переживший 401 = конец сессии → экран логина (отложенный, если идёт звонок) |
| 🟠 Подмена инсталлятора между записью и запуском | [UpdateService.cs:245](OrbitalSIP/Services/UpdateService.cs:245) | Скачивание в свежий каталог с непредсказуемым именем, `FileMode.CreateNew` + `FileShare.None`, сверка размера с `size` из release API, проверка что download URL ведёт на GitHub. Заодно стрим на диск вместо ~90 МБ в памяти |
| 🟡 Сырые тела ответов в UI-баннере | [HttpErrorNotifier.cs:33](OrbitalSIP/Services/HttpErrorNotifier.cs:33) | URL и тело — только в лог; в баннер идёт статус, длина ограничена 160 символами |
| 🟡 PII в логах | [LogRedaction.cs](OrbitalSIP/Models/LogRedaction.cs), `CallInfoService`, `LeadService`, `StatusService` | Номера маскируются (`+992*******67`), полные тела ответов на успешном пути не пишутся. Самым крупным источником был `CallInfoService`, писавший весь CRM-профиль звонящего на каждый отвеченный звонок |

### Корректность

| Что | Где | Суть фикса |
|-----|-----|------------|
| 🟠 `Environment.Exit(0)` мимо всей очистки | [MainWindow.axaml.cs:769](OrbitalSIP/MainWindow.axaml.cs:769) | `desktop.Shutdown()`. Теперь поднимается `desktop.Exit` → `SipService.Dispose()`: снятие регистрации на PBX, BYE активному звонку, снятие keyboard hook, флаш Sentry |
| 🟠 Сохранение настроек рвёт активный звонок | [SipService.cs:70](OrbitalSIP/Services/SipService.cs:70) | `Start()` кладёт трубку до пересборки транспорта + верификация выхода в `Idle` |
| 🟠 Утечка `DispatcherTimer` на смене вида | [ActiveCallView.axaml.cs:107](OrbitalSIP/Views/ActiveCallView.axaml.cs:107), [ActiveCallWidgetView.axaml.cs:49](OrbitalSIP/Views/ActiveCallWidgetView.axaml.cs:49) | `_timer.Stop()` в `OnDetachedFromVisualTree`. У виджета override отсутствовал вовсе |
| 🟠 Неатомарный `OnCallEnded` | [SipService.cs](OrbitalSIP/Services/SipService.cs) | Переход захватывается внутри `_lock`; `CleanupMedia` идемпотентен (забирает поля под локом, закрывает свои копии); `_state` — `volatile` |
| 🟠 BYE принимался от любого диалога | [SipService.cs:358](OrbitalSIP/Services/SipService.cs:358) | Сверка Call-ID с активным диалогом, чужому — 481 |
| 🟠 Хоткей уходил в шелл вместе с действием | [GlobalHotkeyService.cs:184](OrbitalSIP/Services/GlobalHotkeyService.cs:184) | Комбинация с модификатором поглощается. `Alt+Escape`/`Alt+Enter`/`Alt+Space` — системные шорткаты Windows, и сброс звонка заодно переключал окна. Биндинг без модификатора **не** поглощается: иначе буква стала бы ненабираемой во всей системе |
| 🟠 Баннер верификации мигал и исчезал | [SurveyDialog.axaml.cs:414](OrbitalSIP/Views/SurveyDialog.axaml.cs:414) | Результат сверки передаётся следующему рендеру вместо гонки с ним. Раньше два `UiPost` попадали в один проход диспетчера, а `RenderNode` безусловно гасит баннер — оператор **никогда** не видел «Несоответствие данных» на нетерминальных узлах, то есть ровно там, где это ещё может изменить разговор |
| 🟡 Гонки в `CallAsync`/`AnswerAsync`/`ToggleHold`/`SendDtmfAsync` | [SipService.cs](OrbitalSIP/Services/SipService.cs) | Переходы под локом, события — вне; снапшот `_mediaSession` вместо null-check поля; откат `ToggleHold` при сбое re-INVITE |
| 🟡 UAS не reject'ился при сбое аудио | [SipService.cs](OrbitalSIP/Services/SipService.cs) | `TryRejectPending` шлёт 480 на всех путях аварийного выхода из ответа. Раньше звонящий слушал гудки до своего таймаута |
| 🟡 Баннер ошибки раз в 20 с при мёртвом бэкенде | [StatusService.cs](OrbitalSIP/Services/StatusService.cs), [PollBackoff.cs](OrbitalSIP/Models/PollBackoff.cs) | Экспоненциальный бэкофф 20 с → 5 мин, уведомление только на первый сбой серии |
| 🟡 `async void` обработчики кликов | [SafeHandler.cs](OrbitalSIP/Views/SafeHandler.cs), [ActiveCallView.axaml.cs:370](OrbitalSIP/Views/ActiveCallView.axaml.cs:370) | Обёртка логирует исключение вместо ухода в `AppDomain.UnhandledException`; `CreateLeadAsync` разделён так, что `finally` возвращает кнопку при любом исходе кроме созданного лида |
| 🟡 `SmsService` мимо общего пула | [SmsService.cs:37](OrbitalSIP/Services/SmsService.cs:37) | Перешёл на `BackendHttp.Client`: один пул сокетов, `PooledConnectionLifetime`, и refresh-токен теперь работает и для SMS |
| 🟡 `HttpResponseMessage` без `using` | 7 файлов | `using var response = ...` везде |
| ⚪ `Trim()` на пароле | [LoginView.axaml.cs:51](OrbitalSIP/Views/LoginView.axaml.cs:51) | Снят — пробелы по краям легальны в пароле |
| ⚪ Неатомарная запись настроек | [SipSettings.cs:82](OrbitalSIP/Services/SipSettings.cs:82) | Запись во временный файл + `File.Move(overwrite)`. Обрыв на середине больше не оставляет обрезанный файл, который `Load()` молча заменяет дефолтами |
| ⚪ Мёртвая кнопка Recents в панели звонка | [ActiveCallView.axaml.cs:287](OrbitalSIP/Views/ActiveCallView.axaml.cs:287) | Warning CS0067 указывал на живой баг: `MainWindow` подписывался на `OnRecentsRequested`, а вид его никогда не поднимал — кнопка в нижней навигации не делала ничего. Событие проброшено из `BottomNavControl`, как в `ExpandedView` |
| ⚪ Утечка PII в статическом кэше | [ActiveCallView.axaml.cs:65](OrbitalSIP/Views/ActiveCallView.axaml.cs:65) | `ForgetCachedCall()` вызывается при переходе звонка в `Idle` и при истечении сессии. Кэш должен переживать collapse/expand, но не звонок и не логаут |
| ⚪ Корневые `bin`/`obj` не в `.gitignore` | [.gitignore](.gitignore:3) | Добавлены |
| ⚪ Варнинги сборки | [SurveyDialog.axaml.cs:70](OrbitalSIP/Views/SurveyDialog.axaml.cs:70), `GainAudioEndPoint` | AVLN:0005 — добавлен parameterless-конструктор для XAML-загрузчика, не запускающий `InitAsync` (как `loadTemplates:false` у `SmsComposeDialog`). CS0067 — см. выше. 4× CS8618 на событиях — `= null!` |

**Новые тесты (+78):** [AccessTokenLifetimeTests](OrbitalSIP.Tests/AccessTokenLifetimeTests.cs) — границы skew, отсутствующий `exp`, непредставимые значения; [PollBackoffTests](OrbitalSIP.Tests/PollBackoffTests.cs) — удвоение, потолок, отрицательный счётчик; [LogRedactionTests](OrbitalSIP.Tests/LogRedactionTests.cs) — маска сохраняет различимость номеров и не содержит середины; [GlobalHotkeyServiceTests](OrbitalSIP.Tests/GlobalHotkeyServiceTests.cs) — парсер комбинаций и правило «голая клавиша не регистрируется»; [BackendHttpTests](OrbitalSIP.Tests/BackendHttpTests.cs) — детект http/https, disposal `SmsService`.

---

## 🔴 Открыто — транспорт без шифрования

Июньский обзор описывал это как «TLS-валидация отключена в 8 файлах» и предлагал убрать колбэк. **Оценка была неверной.** Развёрнутая конфигурация:

```
"BackendUrl": "http://10.10.103.46"
"Transport":  "UDP"
```

TLS там нет вообще, поэтому `RemoteCertificateValidationCallback => true` никогда не вызывался, и его удаление ничего не меняло. Реальная экспозиция проще и шире:

- `POST /api/auth/login` — логин и пароль оператора в открытом виде;
- `GET /api/auth/sip-credentials` — **SIP-пароль** в открытом виде;
- Bearer-токен на каждом последующем запросе;
- PII лидов, CDR, номера звонящих;
- SIP-сигнализация по UDP без TLS, RTP без SRTP — разговор тоже в открытую.

Достаточно снифера в том же сегменте: подставной сертификат не нужен, MITM не нужен.

**Клиентская часть подготовлена:** мёртвый колбэк удалён, применяется дефолтная валидация ([BackendHttp.cs:37](OrbitalSIP/Services/BackendHttp.cs:37)), при `http://` BackendUrl один раз за запуск пишется предупреждение в лог ([BackendHttp.cs:75](OrbitalSIP/Services/BackendHttp.cs:75)) — из двух точек: старт приложения и момент отправки логина.

**Что осталось — вне этого репозитория:** поднять https на бэкенде, перевести SIP на TLS + SRTP. До этого никакой клиентский код проблему не закрывает.

---

## 🟠 Открыто

### Инсталлятор не подписан
[UpdateService.cs:321](OrbitalSIP/Services/UpdateService.cs:321). В сборке нет шага `signtool` — ни в [build.ps1](build.ps1), ни в [OrbitalSIP.iss](installer/OrbitalSIP.iss). Поэтому проверка Authenticode перед запуском **намеренно не включена**: она сломала бы каждое обновление.

Сейчас факт отсутствия подписи пишется в лог при каждом обновлении, а окно подмены файла закрыто (непредсказуемый каталог, `CreateNew`, сверка размера). Но целостность скачанного бинаря по-прежнему держится только на TLS до GitHub.

**Фикс — в пайплайне сборки:** подписать инсталлятор, затем включить проверку в `LogAuthenticode` (превратить лог в отказ).

### Хоткеи: `RegisterHotKey` написан, но выключен по умолчанию
[GlobalHotkeyService.cs:192](OrbitalSIP/Services/GlobalHotkeyService.cs:192), флаг [SipSettings.cs:67](OrbitalSIP/Services/SipSettings.cs:67).

Коллизия с шеллом уже закрыта на стороне хука, callback удешевлён. Архитектурная часть — `WH_KEYBOARD_LL` пропускает через процесс **каждое нажатие на машине**, и зависание колбэка дольше `LowLevelHooksTimeout` подвешивает ввод во всей системе — решается реализованным путём `RegisterHotKey`: выделенный поток с `GetMessage`-циклом, регистрация всё-или-ничего, повторная регистрация по `WM_REREGISTER` при сохранении настроек.

**Почему по умолчанию выключено:** этот код нельзя выполнить в headless-прогоне. Автоматический откат на хук закрывает отказ регистрации (комбинация занята другой программой — сценарий, которого у хука не было вовсе), но не «зарегистрировались, а цикл сообщений молча не доставляет». Оператор, потерявший хоткей сброса посреди смены, — худший исход, чем невежливый хук.

**Чтобы включить:** `"UseHotkeyRegistration": true` в `sip-settings.json`, затем прогнать все четыре хоткея при неактивном окне, при активном окне, и проверить, что комбинация больше не уходит в шелл. После успешного прогона — поменять дефолт в `SipSettings`.

Ограничение по конструкции: если хоть один биндинг без модификатора, путь регистрации не используется вовсе (`RegisterHotKey` поглощает то, что занял, и голая буква перестала бы набираться во всей системе). Правило покрыто тестами.

---

## ⚪ Открыто

- [ActiveCallView.axaml.cs:318](OrbitalSIP/Views/ActiveCallView.axaml.cs:318) — захардкоженные `"Unmute"/"Mute"` посреди i18n-интерфейса. Ключей нет ни в одном из четырёх файлов; нужны переводы на `kk`/`tg`/`uz`.
- `SessionExpired` добавлен только в [ru.json](OrbitalSIP/Assets/i18n/ru.json). В `kk`/`tg`/`uz` подставляется русский дефолт — нужен перевод.
- Корневой `obj/` всё ещё в индексе — 16 файлов от мёртвого `test_icon.csproj`: `git rm -r --cached obj`. `test.py` в корне — двухстрочный скрап.
- 5× CS8618 в [GainAudioEndPoint.cs:154](OrbitalSIP/Services/Audio/GainAudioEndPoint.cs:154) оставлены намеренно. Предупреждения честные: `_waveInEvent`, `_waveOutEvent`, `_waveProvider`, `_waveSinkFormat`, `_waveSourceFormat` инициализируются условно (`disableSource`/`disableSink`), и это приложение всегда передаёт `false`. Перевод в nullable — 39 мест в аудио-хотпате, который здесь не прогоняется; ошибочный `?.` в capture-колбэке молча съест звук. Стоит делать вместе с тестами на аудио, не вслепую.
- Avalonia приколочена к **11.0.0** (середина 2023). [SmsComposeDialog](OrbitalSIP/Views/SmsComposeDialog.axaml.cs:412) несёт три обхода багов `AutoCompleteBox`, один из них лезет в визуальное дерево контрола за приватной template-частью. Апгрейд снимет костыли — но и сломает их, если внутренности переедут. Планировать отдельно.

---

## Проверка самих правок

Изменения этого обзора прогнаны через состязательное ревью (6 областей, находки проверялись отдельным скептиком с презумпцией «не баг»): **26 кандидатов → 5 подтверждено, 3 опровергнуто**. Подтверждённые были реальными регрессиями, внесёнными правками, и исправлены здесь же:

| Что нашли | Почему это важно |
|-----------|------------------|
| `_timer = null` в `OnDetachedFromVisualTree` убивал таймер звонка **навсегда** | Анимированная смена вида переносит контрол из `OverlayHost` в `Host` — это detach+attach, restart'а не было. Таймер показывал бы `00:00` весь разговор, и замороженное значение уезжало в следующий вид. Исправлено переходом на `Stopwatch` (остановленный таймер больше не может потерять время) + restart на attach |
| Сохранение настроек теряло `RefreshToken` | Обработчик копировал 4 поля сессии из списка, написанного руками; новое поле в список не попало — сохранение настроек молча обезоруживало обновление токена на всю смену. Заменено на `SipSettings.CopySessionFrom` + рефлексивный тест, падающий при добавлении следующего такого поля |
| Refresh шёл под токеном отмены случайного вызывающего | Ротация уже совершена на сервере, а отмена одного запроса бросала клиент со сгоревшим refresh-токеном — сессия умирала для всех. Теперь у refresh собственный дедлайн |
| 401 = мгновенный логаут | Запрос мог уйти с токеном, который другой поток только что проротировал. Теперь 401 **проверяется** принудительным refresh; сессию закрывает только 401 от самого `/auth/refresh` |
| Повторная регистрация хоткеев обходила guard на голую клавишу | Правка хоткея на голую букву в рантайме зарегистрировала бы её — и `RegisterHotKey` поглотил бы букву во всей системе |

Из непроверенных кандидатов дополнительно исправлены: баннер верификации оставался на финальной странице; `ResetBackoff` срабатывал до разбора тела; каталог обновления не подчищался; `Start()` объявлял `Idle` дважды; неотвеченный входящий leg оставался без финального ответа; URL с номером звонящего попадал в лог через `NotifyHttpError`; короткие номера маскировались тремя звёздами; анимация перетирала геометрию экрана логина; `Matches` игнорировал Shift/Win и с новым поглощением съедал неназначенные комбинации.

Уже после этого нашлась ещё одна, внесённая самими правками: `Hangup()` отклонял `uas`, а добавленный `RollbackToIdle` отклонял его повторно — два финальных ответа на один INVITE. Исправлено.

## Чего в этом проходе не проверяли

Изменения в `SipService`, `GlobalHotkeyService`, `SurveyDialog`, `UpdateService` и потоке логина проверены сборкой и юнит-тестами, но **не прогонялись на живом стенде**. Ручной прогон:

1. Входящий и исходящий звонок; hold / mute / transfer.
2. Сохранение настроек **во время** звонка — звонок должен корректно завершиться, а не зависнуть в `Active`.
3. Выход из приложения всеми четырьмя путями — проверить на PBX, что регистрация снята.
4. Четыре хоткея при неактивном окне; убедиться, что `Alt+Space` больше не открывает системное меню чужого окна.
5. Анкета с расхождением данных на **нетерминальном** узле — баннер «Несоответствие данных» должен остаться на экране.
6. Обновление: скачивание, сверка размера, запуск инсталлятора; в `app.log` — строка про отсутствие подписи.
7. Долгая сессия (> 2 суток) или подмена `exp` — проверить, что токен обновляется молча, а отзыв токена приводит к экрану логина, причём не посреди звонка.

---

## Что в проекте сделано хорошо

- Логика вынесена из view в тестируемые модели (`SmsComposeState`, `LeadCallPanelPresenter`, `ActiveCallSmsLifecycle`, `WindowPlacement`, `JitterTrim`) — для UI-приложения это правильный шов, и он же позволил покрыть тестами всю новую логику этого прохода.
- `AsyncLogWriter`/`BoundedLogQueue`/`LogRotation` — ограниченная очередь, не бросает, ротация всегда прогрессирует, финальный дренаж на shutdown.
- Комментарии объясняют **почему**, а не что, включая обходы багов фреймворка и обоснование зависимостей в `.csproj`.
- `SmsService` парсит строго и принципиально не отражает сырые тела в исключениях.

---

# Дополнение от 20 августа 2026

Проход поверх обзора от 10 августа. Проверялось на `main` **после** мержа PR #47/#48/#49
(`554bd01`), тесты 439 зелёных.

## 🔴 Ветки, не покидавшие диск

Пятнадцать коммитов существовали только в локальных ветках, без upstream. Среди них —
весь обзор от 10 августа вместе с исправлениями, которые он описывает: гонки переходов
состояния, BYE по Call-ID, `desktop.Shutdown()`, таймеры, приватность логов, ротация
refresh-токена. Операторы всё это время работали на `v1.0.34` без единого из них.

**Прямая цена:** утечка winmm-хэндлов устройства вывода была независимо переоткрыта и
исправлена **дважды** — второй раз просто потому, что первый фикс никто не видел.

Побочный эффект того же: этот файл на `main` был версией от июня, а актуальный обзор от
10 августа лежал на ветке. Кто читает `CODE_REVIEW.md`, находясь на ветке от `main`,
получает устаревшую картину и делает неверные выводы — в частности, «TLS-валидация
закрыта» вместо «транспорта без шифрования как не было, так и нет» (см. раздел
«Открыто — транспорт без шифрования», он остаётся в силе).

Проверять перед каждым релизом:

```bash
git for-each-ref --format='%(refname:short)|%(upstream:short)' refs/heads \
  | while IFS='|' read -r b u; do [ -z "$u" ] && echo "$b: $(git rev-list --count origin/main..$b)"; done
```

Ветки со статусом `[gone]` в этот список не попадают потерянными — у них удалён remote
после squash-мержа, содержимое уже в `main`. Не поднимать ложную тревогу.

**Статус: закрыто.** Всё запушено и смержено 20.08.

## Живо

| Severity | Место | Последствие |
|---|---|---|
| 🟠 | `UpdateService.cs:127` | Проверка `State != CallState.Idle` стоит до `DownloadAndInstallAsync` (`:135`), а `Process.Start` инсталлятора — на `:302`. Между ними, то есть на всё время многоминутной загрузки, состояние не перепроверяется: обновление, начатое в простое, убивает звонок, начавшийся во время скачивания. Ставить повторную проверку непосредственно перед запуском |
| 🟡 | `SipService.cs:897` | `CleanupMedia` сбрасывает `_audioPaused`, но не `IsMuted` и не `IsOnHold`. Виджет читает `App.SipService.IsMuted` при создании, поэтому следующий звонок оператор видит приглушённым при живом микрофоне — сказанное в сторону уходит абоненту. Сбрасывать оба там же |
| 🟡 | `SipService.cs:628` | `ApplyAudioState` в блоке `catch` пишет `_audioPaused = IsMuted`, помечая состояние достигнутым даже когда `ResumeAudio()` упал. Ветка `!IsMuted && _audioPaused` после этого не сработает никогда — микрофон мёртв до конца сессии, а в интерфейсе оператор размучен. Триггер реальный: `StopRecording` у NAudio асинхронный, быстрый двойной тумблер mute даёт `InvalidOperationException`. Не помечать состояние достигнутым при сбое |

## Проверено и НЕ является багом

Записано, чтобы следующий проход не поднимал заново.

- **Утечка аудио-эндпоинта на путях отказа `TryCreateAudio`.** Выглядит как баг: поле
  `_audioEndPoint` присваивается до того, как остальная настройка может бросить. Но оба
  вызывающих (`CallAsync:241`, `AnswerAsync:439`) идут через `RollbackToIdle()`, который
  на `:333` зовёт `CleanupMedia()`. Освобождение происходит.

## Границы прохода

Прочитано целиком: `SipService.cs` (обе версии — до и после мержа), `GainAudioEndPoint.cs`,
ключевая часть `UpdateService.cs`, пути выхода и сохранения настроек в `MainWindow.axaml.cs`.

**`Views` (37 файлов, ~97k токенов, почти половина кодовой базы) не читался.** Это
главная непокрытая область: ни один из проходов не прошёл её целиком. Следующий аудит
имеет смысл начинать именно с неё.

Оценка объёма для планирования: весь код ~275k токенов, из них не-UI логика
(`Services` + `Models` + корень) ~90k и помещается в контекст целиком.

---

# Дополнение от 20 августа 2026 (проход №2) — слой `Views`

Проверялось на `origin/main` = `731b273`. Сборка зелёная, тесты **439/439**.

**Поправка к вводным.** `origin/main` ≠ тег `v1.0.34` (`91c1a99`): main опережает тег на 41 коммит
и 55 файлов, `SipService.cs` вырос с 878 до 1130 строк. Ветки `fix/audio-device-handle-leak`,
`fix/playback-device-index`, `feat/sms-panel-redesign` **уже смержены** (PR #47/#48/#49), их не
нужно ждать. Отдельно: `version.txt` и на теге, и на `main` содержит `1.0.32` — тег `v1.0.34`
срезан без бампа версии, поэтому `UpdateService.CurrentVersion` у операторов на два релиза
младше реальности и обновление предложится раньше, чем нужно.

Всё ниже проверено на `origin/main`.

## Границы прохода

**Прочитано целиком:** все 22 файла `OrbitalSIP/Views/*.cs` (главная цель прохода),
`MainWindow.axaml.cs`, `App.axaml.cs`, `Program.cs`, `SipService.cs`, `GainAudioEndPoint.cs`
и остальные файлы `Services/Audio`, `AudioDeviceCheck.cs`, `StatusService.cs`,
`AuthRefreshHandler.cs`, `BackendHttp.cs`, `HttpErrorNotifier.cs`, `SipSettings.cs`,
`I18nService.cs`, список тестов целиком + `GainAudioEndPointLifecycleTests`,
`WaveOutDevicesTests`.

**Точечно (по grep, не целиком):** `UpdateService.cs`, `FlowsService.cs`, `LeadService.cs`,
`TaskService.cs`, `CallInfoService.cs`, `ScriptService.cs`, `SmsService.cs`,
`GlobalHotkeyService.cs`, `BackendAuth.cs` — читались только те методы, от которых зависела
конкретная находка (в основном: ловит ли сервис исключения внутри себя).

**Не смотрел вообще:** `.axaml`-разметка (кроме точечных grep по именам контролов и `Tag`),
`Models/*` кроме упомянутых, `LoggedCallService`, `SoundService`, `JwtDecoder`,
`LeadCallPanelPresenter`, `CallInfoPresenter`, `installer/`, `build.ps1`, CI.

**Живой стенд не использовался.** Ни одна находка ниже не воспроизведена на реальном
телефоне — всё выведено из чтения кода. Сценарии сформулированы, но не отсняты.

## 🔴 Открыто

### Отменённый входящий звонок не освобождает аудиоустройства
[SipService.cs:400](OrbitalSIP/Services/SipService.cs:400)

`ServerCallCancelled` — **единственный** переход в `Idle` во всём файле, который идёт мимо
`OnCallEnded()`/`RollbackToIdle()`, то есть **единственный, который не зовёт `CleanupMedia()`**.
Он обнуляет `_activeCall` и `_pendingUas` и объявляет `Idle`, а `_audioEndPoint` и
`_mediaSession` остаются висеть.

Сценарий: оператор жмёт «Ответить», `AnswerAsync` уже прошёл `TryCreateAudio()`
([:439](OrbitalSIP/Services/SipService.cs:439)) и ждёт на `await ua.Answer(...)`
([:458](OrbitalSIP/Services/SipService.cs:458)). В этот момент звонящий из очереди сдаётся и
кладёт трубку → приходит CANCEL → срабатывает этот обработчик → `_state = Idle`. `AnswerAsync`
просыпается, видит `Idle` на [:480](OrbitalSIP/Services/SipService.cs:480) и выходит **без
`CleanupMedia`**. `GainAudioEndPoint` не диспозится: один winmm-хэндл вывода и один ввода
остаются открытыми до конца процесса. Следующий звонок открывает новую пару.

Это ровно тот дефект, который чинили дважды (`CleanupMedia` → `Dispose`, см.
[GainAudioEndPoint.cs:629](OrbitalSIP/Services/Audio/GainAudioEndPoint.cs:629)), но оба раза
чинили *путь*, а не *обход пути*. Итог для оператора тот же и описан в комментарии к самому
`Dispose`: когда драйвер перестаёт выдавать хэндлы, `waveOutOpen` падает, динамики не
открываются, **оператор не слышит собеседника, а PBX пишет нормальный двусторонний разговор**.
Это и есть жалоба, ради которой затевался аудит.

Частота: «звонящий бросил трубку ровно в момент ответа» в загруженной очереди — ежедневное
событие, но сколько раз за смену, неизвестно: замеров нет.

**Фикс:** заменить тело обработчика на `OnCallEnded()` — он идемпотентен, сам захватывает
переход под локом и сам зовёт `CleanupMedia()`. Отдельная ветка здесь не нужна.

## 🟠 Открыто

| Severity | Место | Последствие для оператора | Фикс |
|---|---|---|---|
| 🟠 | [ActiveCallView.axaml.cs:38](OrbitalSIP/Views/ActiveCallView.axaml.cs:38) | **Кнопка Hold инвертируется после сворачивания/разворачивания.** `_muted`/`_onHold` — поля со значением по умолчанию `false`, и конструктор их **не** инициализирует из сервиса: [:103](OrbitalSIP/Views/ActiveCallView.axaml.cs:103) красит только `StatusLabel`/`StatusDot` через `SetStatus`, а `HoldLabel` и цвет кнопки не трогает. `ActiveCallWidgetView` наоборот принимает `isMuted`/`isOnHold` конструктором ([MainWindow.axaml.cs:587](OrbitalSIP/MainWindow.axaml.cs:587)) — панель этого не делает ([MainWindow.axaml.cs:611](OrbitalSIP/MainWindow.axaml.cs:611)). Сценарий: оператор ставит на удержание из мини-виджета → разворачивает панель → кнопка говорит «Hold» (синяя) на удержанном звонке; жмёт её, чтобы снять удержание — звонок действительно снимается, но кнопка становится красной «Resume». Дальше до конца звонка кнопка показывает обратное состоянию. Оператор либо оставляет клиента на удержании, думая что снял, либо ставит на удержание, думая что снял | Передавать `App.SipService.IsMuted`/`IsOnHold` в конструктор `ActiveCallView`, как это уже сделано для виджета, и красить `HoldLabel`/`MuteLabel` из них. Плюс: [MainWindow.axaml.cs:633](OrbitalSIP/MainWindow.axaml.cs:633) игнорирует аргумент события (`(_, __) => ToggleHold()`) — из-за этого рассинхрон не самолечится на первом клике. Сделать `SetHold(bool)` по образцу `SetMuted(bool)` |
| 🟠 | [SipService.cs:575](OrbitalSIP/Services/SipService.cs:575) | **DTMF не отправляется вообще.** `SendDtmfAsync` не имеет ни одного вызывающего во всём репозитории (grep по `*.cs` и `*.axaml`, без `bin`/`obj`/`.claude/worktrees`). Кнопка «Клавиатура» на панели звонка ведёт в [MainWindow.axaml.cs:635](OrbitalSIP/MainWindow.axaml.cs:635) → `ShowDialer()`, а тот на [:411](OrbitalSIP/MainWindow.axaml.cs:411) при `Active`/`OnHold` пересобирает **ту же самую** `ActiveCallView`. То есть кнопка визуально ничего не делает. Оператор не может пройти IVR партнёра, ввести добавочный, PIN конференции или код в тональном меню — звонок приходится бросать | Показать тональную клавиатуру поверх панели и завести цифры в `SendDtmfAsync`. Сейчас метод — мёртвый код с полной защитой от гонок, написанный под несуществующий вызов |
| 🟠 | [BottomNavControl.axaml.cs:27](OrbitalSIP/Views/BottomNavControl.axaml.cs:27) | **Утечка целого дерева view на каждую навигацию.** Подписка на `App.Updater.UpdateAvailable` (синглтон уровня процесса, [App.axaml.cs:34](OrbitalSIP/App.axaml.cs:34)) в конструкторе, без отписки — у контрола нет `OnDetachedFromVisualTree` вообще. `BottomNavControl` лежит в `ActiveCallView`, `ExpandedView`, `RecentsView` и `SettingsView`, а `MainWindow` строит каждый из них заново на любой смене экрана. Каждое разворачивание, каждый звонок, каждый заход в настройки навсегда прикалывает дерево контролов к статическому событию. За смену — сотни. Хуже, чем память: `ActiveCallView` держит `_leadContext` (ФИО, телефон, лид, владелец) в собственном поле — статический `ForgetCachedCall()` его не чистит, так что **PII каждого звонящего за смену остаётся в процессе**, несмотря на закрытую в прошлом проходе находку про кэш | `OnDetachedFromVisualTree` с отпиской. Тот же дефект и там же по смыслу — [SettingsView.axaml.cs:223](OrbitalSIP/Views/SettingsView.axaml.cs:223) (лямбда на `UpdateAvailable`, захватывает кнопку) и [SettingsView.axaml.cs:27](OrbitalSIP/Views/SettingsView.axaml.cs:27) (`App.SipService.RegistrationError`, без отписки; при следующем сбое регистрации N мёртвых `SettingsView` пишут каждый в свой отсоединённый `StatusLabel`) |
| 🟠 | [RecentsView.axaml.cs:94](OrbitalSIP/Views/RecentsView.axaml.cs:94) | **UTC-дата как местный рабочий день.** `DateTime.UtcNow.Date` → окно запроса `[D 00:00Z, D 23:59Z]`, в местном времени (UTC+5) это `[D 05:00, D+1 04:59]`. Первые пять часов каждых местных суток в «сегодня» не попадают. Оператор ночной смены открывает «Недавние» и не видит звонков с 00:00 до 05:00 — перезвонить клиенту, с которым говорил час назад, не по чему. Рядом [OperatorStatsControl.axaml.cs:76](OrbitalSIP/Views/OperatorStatsControl.axaml.cs:76) спрашивает у бэкенда `range=today`, то есть считает «сегодня» по-своему: счётчик и список расходятся | Считать границы от местной полуночи и переводить в UTC (`DateTime.Today` → `.ToUniversalTime()`), либо, что честнее, тоже переложить на бэкенд, как это уже сделано в статистике |
| 🟠 | [SipService.cs:388](OrbitalSIP/Services/SipService.cs:388) | **Входящий INVITE проверяет `State == Idle` вне лока.** Комментарий в `CallAsync` ([:220](OrbitalSIP/Services/SipService.cs:220)) обещает, что захват `Idle → Ringing` под локом заставит обработчик INVITE «увидеть Ringing и не поднимать второй leg» — но обработчик читает `State` без лока на `:388`, пишет `_activeCall`/`_pendingUas` в локе только на [:414](OrbitalSIP/Services/SipService.cs:414), а `SetState(IncomingRinging)` на [:421](OrbitalSIP/Services/SipService.cs:421) пишет `_state` вообще без лока. Обратное направление гонки открыто: обработчик прочитал `Idle` → `CallAsync` захватил `Ringing` → обработчик перезаписал `_activeCall` входящим агентом и объявил `IncomingRinging`. Исходящий `ua` при этом ещё внутри `await ua.Call()`, но из `_activeCall` уже пропал: `Hangup()` до него не дотянется, звонок «зависает» до таймаута. «Оператор набирает номер ровно когда прилетает входящий» — в очереди обычная ситуация | Захватывать `Idle → IncomingRinging` внутри `_lock` тем же приёмом, что и в `CallAsync`, и отвечать 486 на INVITE, который этот захват не выиграл |
| 🟠 | [SipService.cs:365](OrbitalSIP/Services/SipService.cs:365) | **Проверка BYE по Call-ID не работает, пока звонок не отвечен.** `activeCallId = activeUa?.Dialogue?.CallId` — у исходящего `SIPUserAgent` `Dialogue` появляется только после ответа, а `_activeCall` присваивается до `ua.Call()` ([:251](OrbitalSIP/Services/SipService.cs:251)). Значит на всём окне `Ringing` `activeCallId == null`, условие `activeCallId != null && ...` не выполняется, и **любой** BYE проваливается в `OnCallEnded()` и рвёт набираемый звонок. Ровно сценарий, который описывает комментарий к самому фиксу ([:356](OrbitalSIP/Services/SipService.cs:356)): ретрансмит BYE от **предыдущего** звонка (по UDP таймер F тянет до 32 с, если наш 200 OK потерялся) прилетает во время набора следующего и кладёт его. Плюс тривиальный спуфинг по UDP из того же сегмента | Сверять с сохранённым Call-ID исходящего INVITE, а не только с `Dialogue`. При `activeCallId == null` и непустом `byeCallId` считать BYE чужим, а не своим |
| 🟠 | [LoginView.axaml.cs:38](OrbitalSIP/Views/LoginView.axaml.cs:38) | **Двойной Enter = два параллельных логина.** `userBox.KeyDown += async (s, e) => { if (e.Key == Key.Enter) await AttemptLogin(); }` (и то же на [:42](OrbitalSIP/Views/LoginView.axaml.cs:42)) — `async void`, `e.Handled` не ставится, реентерабельность не проверяется. `SetBusy(true)` ([:162](OrbitalSIP/Views/LoginView.axaml.cs:162)) гасит только кнопку, поля ввода остаются активны. Автоповтор клавиши или просто быстрый второй Enter → два `POST /api/auth/login`. Кто из двух продолжений допишет `settings.RefreshToken` последним — гонка; если бэкенд ротирует refresh-токены при логине, в настройках может осесть уже отозванный, и сессия умрёт посреди смены с экраном логина. Плюс `SipService.Start(settings)` отработает дважды — транспорт пересобирается дважды | Флаг реентерабельности в `AttemptLogin` (как `_isFetching` в `StatusService`) + `e.Handled = true` |
| 🟠 | [OrbitalSIP.csproj:58](OrbitalSIP/OrbitalSIP.csproj:58) | **SIPSorcery 10.0.13 — две известные уязвимости high.** Сборка печатает 4× `NU1903` (по два на проект): `GHSA-jwjp-4649-v8jp` и `GHSA-pfvm-w89x-94jw`. Это библиотека, которая разбирает SIP и RTP из сети — то есть недоверенный ввод по UDP с открытого порта. В прошлых обзорах не фигурировало | Поднять SIPSorcery до версии с фиксом (сверить с advisories) и прогнать регресс по звонкам. Заодно `System.Net.Http` 4.3.4 и `System.Text.RegularExpressions` 4.3.1 в [csproj:81](OrbitalSIP/OrbitalSIP.csproj:81) — это out-of-band пакеты эпохи .NET Core 1.x, на net8.0 они не нужны и только маскируют, какая реализация реально в рантайме |
| 🟠 | [WaveOutDevicesTests.cs:19](OrbitalSIP.Tests/WaveOutDevicesTests.cs:19) | **Регресс-тесты на ту самую утечку аудио ничего не проверяют на headless-машине.** `Assert.True(WaveOutDevices.Count >= 0)` не может упасть: `Count` возвращает `waveOutGetNumDevs()` либо 0 из `catch`. То же на [:66](OrbitalSIP.Tests/WaveOutDevicesTests.cs:66). Серьёзнее — [GainAudioEndPointLifecycleTests.cs:89](OrbitalSIP.Tests/GainAudioEndPointLifecycleTests.cs:89) `RepeatedCreateAndDispose_KeepsOpeningTheDevice`: на машине без устройств вывода `IsPlaybackDeviceOpen` всегда `false`, `firstOpen == false`, и 64 итерации сверяют `false == false`. Тест зелёный при полностью возвращённой утечке. То же вырождение у `Dispose_ClosesThePlaybackDevice` и `IsPlaybackDeviceOpen_MatchesWhetherThisMachineHasSpeakers` | Пропускать (`Skip`) при `WaveOutDevices.Count == 0`, чтобы отсутствие покрытия было видно, а не выглядело как зелёный прогон. Иначе CI даёт ложную уверенность ровно по тому классу дефектов, который породил жалобу |

## 🟡 Открыто

| Severity | Место | Последствие для оператора | Фикс |
|---|---|---|---|
| 🟡 | [OperatorStatsControl.axaml.cs:44](OrbitalSIP/Views/OperatorStatsControl.axaml.cs:44) | Таймер обновления статистики (2 мин) останавливается в `OnDetachedFromVisualTree`, а `OnAttachedToVisualTree` не переопределён. Контрол живёт в [ExpandedView.axaml:21](OrbitalSIP/Views/ExpandedView.axaml:21), а `ExpandedView` всегда проходит через `StartAnimation`: сначала попадает в `OverlayHost`, потом `CompleteAnimatedContentSwap` переносит его в `Host` — это detach+attach. То есть таймер убивает та самая анимация, которая выводит панель на экран, примерно через 280 мс. Автообновление статистики мертво; цифры меняются только при новом разворачивании. Тот же дефект, что прошлый проход починил в `ActiveCallView`/`ActiveCallWidgetView`, — здесь фикс не применили | Перезапуск в `OnAttachedToVisualTree`, как в двух других контролах |
| 🟡 | [SettingsView.axaml.cs:91](OrbitalSIP/Views/SettingsView.axaml.cs:91) | Сохранённый индекс устройства молча сбрасывается на «по умолчанию», если устройства сейчас нет. `speakerBox.SelectedIndex = _settings.AudioOutDeviceIndex + 1` вне диапазона → Avalonia ставит `-1` → строка ниже принудительно ставит `0`. Любое сохранение настроек после этого записывает `-1` ([:324](OrbitalSIP/Views/SettingsView.axaml.cs:324)). Сценарий: оператор отключил USB-гарнитуру, зашёл в настройки сменить язык, сохранил — выбор динамиков потерян навсегда. Гарнитуру воткнули обратно, а звук идёт в динамики ноутбука: в шумном зале оператор собеседника не слышит. Свежий фикс `PlaybackDevice.IsUsable` существует ровно чтобы пережить временно пропавшее устройство — здесь потеря делается постоянной | Не перетирать сохранённый индекс, если устройство просто отсутствует: показывать его как недоступный пункт и оставлять значение в файле |
| 🟡 | [ActiveCallView.CallInfo.cs:48](OrbitalSIP/Views/ActiveCallView.CallInfo.cs:48) | `_callInfoLoaded = true` выставляется **до** отрисовки и независимо от того, вернул ли сервис `null`. `ToggleCallInfoPanel` ([:29](OrbitalSIP/Views/ActiveCallView.CallInfo.cs:29)) грузит только если `!_callInfoLoaded`. Одна сетевая ошибка → панель «Информация о звонке» показывает «пусто» и **никогда не перезапрашивает** до конца звонка. Кнопки повтора здесь нет (в отличие от панели лида с `LeadRetryBtn`). Оператор не видит кредиты/счета клиента и не может это исправить | Ставить флаг только при `response != null`; либо добавить кнопку повтора |
| 🟡 | [ActiveCallView.axaml.cs:352](OrbitalSIP/Views/ActiveCallView.axaml.cs:352) | Кнопка «копировать» навсегда застревает галочкой при двойном клике: `var originalKind = icon.Kind; icon.Kind = Check; await Task.Delay(1200); icon.Kind = originalKind;` — второй клик внутри 1,2 с захватывает уже `Check` как «оригинал». Четыре места: [ActiveCallView.axaml.cs:352](OrbitalSIP/Views/ActiveCallView.axaml.cs:352), [ActiveCallView.axaml.cs:1083](OrbitalSIP/Views/ActiveCallView.axaml.cs:1083) (кнопка задачи, причём `IsEnabled = true` возвращается **до** вспышки), [ExpandedView.axaml.cs:142](OrbitalSIP/Views/ExpandedView.axaml.cs:142), [IncomingView.axaml.cs:64](OrbitalSIP/Views/IncomingView.axaml.cs:64). Рядом есть правильный образец — [ActiveCallView.CallInfo.cs:205](OrbitalSIP/Views/ActiveCallView.CallInfo.cs:205) восстанавливает константу, а не захваченное значение | Восстанавливать константный `MaterialIconKind`, как в `CallInfo` |
| 🟡 | [ActiveCallView.axaml.cs:968](OrbitalSIP/Views/ActiveCallView.axaml.cs:968) | `SubmitTaskAsync` гасит кнопку задачи и идёт в `await` без `try/finally` — ровно тот шаблон, который прошлый проход починил в `CreateLeadAsync` и описал в комментарии на [:410](OrbitalSIP/Views/ActiveCallView.axaml.cs:410). Вызывается как `_ = SubmitTaskAsync(...)` ([:901](OrbitalSIP/Views/ActiveCallView.axaml.cs:901)), то есть бросок уходит в `UnobservedTaskException`. `TaskService.CreateTaskAsync` сегодня ловит всё внутри, так что это латентно, но кнопка «Задача» умрёт до конца звонка, если это перестанет быть правдой | `try/finally` вокруг, как в `CreateLeadAsync` |
| 🟡 | [SipService.cs:741](OrbitalSIP/Services/SipService.cs:741) | `_audioEndPoint` и `_mediaSession` публикуются **вне `_lock`** (`:741` и [:794](OrbitalSIP/Services/SipService.cs:794)), а `CleanupMedia` забирает их под локом ([:891](OrbitalSIP/Services/SipService.cs:891)). Между двумя присваиваниями окно: `OnCallEnded` с потока SIPSorcery может увидеть новый `_audioEndPoint` и ещё `null` в `_mediaSession` — закроет устройство настраиваемого звонка; либо увидеть оба `null` и не закрыть ничего, а обе новые ссылки осядут уже после. Это **не** та находка, что прошлый проход отклонил (там речь была про пути отказа внутри `TryCreateAudio`, и вывод «`RollbackToIdle` → `CleanupMedia` всё освобождает» верен) — здесь про саму публикацию | Собрать оба объекта локально и опубликовать одним `lock` в конце `TryCreateAudio` |
| 🟡 | [GainAudioEndPoint.cs:336](OrbitalSIP/Services/Audio/GainAudioEndPoint.cs:336) | `_waveOutEvent = waveOut;` и `lock (_renderLock) { _waveProvider = provider; }` — две несвязанные публикации. `InitPlaybackDevice` вызывается и из конструктора, и из `SetAudioSinkFormat`, а тот — из `GotEncodedMediaFrame` ([:555](OrbitalSIP/Services/Audio/GainAudioEndPoint.cs:555)), то есть с потока приёма RTP, параллельно с согласованием SDP на SIP-потоке. При наложении двух `Init` возможна пара «`_waveOutEvent` от одного вызова, `_waveProvider` от другого»: RTP наливается в буфер, который никто не выкачивает — **тишина на весь звонок при живом устройстве и корректной записи на PBX**. Отдельно: `InitPlaybackDevice` не смотрит на `_disposed`/`_isAudioSinkClosed`, так что опоздавший RTP-кадр после `Dispose()` может открыть новое устройство, которое уже никто никогда не закроет (`Dispose` идемпотентен и второй раз не сработает) | Взять `_renderLock` на весь `InitPlaybackDevice`/`DisposePlaybackDevice` и выходить сразу при `_disposed` |
| 🟡 | [MainWindow.axaml.cs:490](OrbitalSIP/MainWindow.axaml.cs:490) | `StartOutgoingCall` — `async void` без проверки `State != Idle` (такая проверка есть в `HandleProtocolDial` на [:147](OrbitalSIP/MainWindow.axaml.cs:147), но не здесь). Enter в поле номера ([ExpandedView.axaml.cs:74](OrbitalSIP/Views/ExpandedView.axaml.cs:74)) не ставит `e.Handled` и не защищён от автоповтора: удержание Enter строит по новой `ActiveCallView` на каждое повторение, каждый раз перезапуская анимацию и сбрасывая таймер звонка на 00:00. Сам SIP защищён — `CallAsync` захватывает `Idle → Ringing` под локом и остальные вызовы возвращают `false` — ломается только то, что видит оператор | Проверять состояние в начале `StartOutgoingCall`, ставить `e.Handled = true` в обработчике Enter |
| 🟡 | [LoginView.axaml.cs:110](OrbitalSIP/Views/LoginView.axaml.cs:110) | Сырые тела ответов эндпоинтов аутентификации пишутся в лог целиком: `Body: {sipError}` для `/api/auth/sip-credentials` и `Body: {errorBody}` для `/api/auth/login` ([:136](OrbitalSIP/Views/LoginView.axaml.cs:136)). Это два эндпоинта, ответы которых по смыслу содержат SIP-пароль и учётные данные. Прошлый проход закрыл тела в баннере и в `CallInfoService`/`LeadService`/`StatusService`, но эти два call-site мимо. Аналогично [OperatorStatsControl.axaml.cs:96](OrbitalSIP/Views/OperatorStatsControl.axaml.cs:96) и [StatusService.cs:223](OrbitalSIP/Services/StatusService.cs:223) — последний пишет полное тело **на успешном пути**, хотя комментарий на [:139](OrbitalSIP/Services/StatusService.cs:139) объясняет, почему так не делают | Прогнать через ту же обрезку/редакцию, что и `HttpErrorNotifier.NotifyHttpError` |
| 🟡 | [MainWindow.axaml.cs:470](OrbitalSIP/MainWindow.axaml.cs:470) | Кнопка «Сохранить» в настройках кладёт активный звонок без предупреждения. Технически это уже почищено (прошлый проход научил `Start()` завершать звонок аккуратно вместо зависания в `Active`), но настройки открываются прямо из нижней навигации панели звонка ([MainWindow.axaml.cs:636](OrbitalSIP/MainWindow.axaml.cs:636)), и для оператора результат — «нажал Сохранить, клиента разорвало» | Прятать/блокировать «Сохранить» при `State != Idle` либо спрашивать подтверждение |

## ⚪ Открыто

- [SurveyDialog.axaml.cs:55](OrbitalSIP/Views/SurveyDialog.axaml.cs:55) — 14 захардкоженных русских строк в i18n-приложении: причины прерывания, «Шаг N», «Анкета», «Несоответствие данных», «Нет данных для сверки», все тексты ошибок. Это сильно больше, чем найденные ранее `"Unmute"/"Mute"` на [ActiveCallView.axaml.cs:373](OrbitalSIP/Views/ActiveCallView.axaml.cs:373).
- i18n-покрытие измерено: `ru` — 201 ключ, `kk`/`tg`/`uz` — по 190. Не хватает одних и тех же 11: `CheckAudio`, `SessionExpired` и девять `audio.*`. **Все они имеют русский дефолт в точке вызова**, так что оператор видит русский текст, а не голый ключ — то есть это мягче, чем звучало в прошлом обзоре, но перевод всё ещё нужен.
- [SipSettings.cs:126](OrbitalSIP/Services/SipSettings.cs:126) — атомарная запись через временный файл сделана, но без `Flush(true)`: NTFS гарантирует атомарность переименования, а не то, что данные временного файла дошли до диска раньше. При отключении питания остаётся ровно тот обрезанный файл, ради которого фикс и писался. Плюс имя `.tmp` фиксированное.
- [StatusPopupControl.axaml.cs:220](OrbitalSIP/Views/StatusPopupControl.axaml.cs:220) — `left.Minutes` вместо `left.TotalMinutes`: для перерыва на 60 минут (максимум в разметке) первую секунду обратный отсчёт показывает `00:00`.
- [SurveyDialog.axaml.cs:607](OrbitalSIP/Views/SurveyDialog.axaml.cs:607) — `verdictText.Text = verdict.Value.GetRawText()`: сырой JSON вердикта бэкенда прямо в интерфейс оператора.
- `async void`-обработчики кликов мимо `SafeHandler` в `SurveyDialog`, `ScriptsDialog`, `SmsComposeDialog`, `IncomingView`, `ExpandedView`, `SettingsView`, `RecentsView`. **Латентно, не живой баг:** проверено, что `FlowsService` (9 публичных async-методов — 9 `catch`), `TaskService`, `CallInfoService`, `UpdateService.CheckAndUpdateAsync`, `AudioDeviceCheck.Probe` ловят всё внутри. Ровно та оговорка, что написана в самом [SafeHandler.cs:17](OrbitalSIP/Views/SafeHandler.cs:17) — но применили обёртку только в `ActiveCallView`.
- [Program.cs:146](OrbitalSIP/Program.cs:146) — `reader.ReadLine()` на named pipe без ограничения длины; строка на гигабайт от локального клиента — это гигабайт в памяти до `NormalizeNumber`. Сам pipe создаётся без явного `PipeSecurity`; на многопользовательской машине (терминальный сервер — обычное дело в КЦ) стоит проверить, кто может к нему подключиться и инициировать исходящий звонок. Содержимое уже обезврежено (`NormalizeNumber` оставляет только цифры и `+*#`), речь только о факте «чужой процесс может заставить софтфон набрать номер».
- [ExpandedView.axaml.cs:31](OrbitalSIP/Views/ExpandedView.axaml.cs:31) — `if (bottomNav != null) if (bottomNav != null)`, косметика.
- Корневой `obj/` по-прежнему в индексе (16 файлов), `test.py` на месте — из прошлого обзора, не сделано.

## Статус находок прошлых проходов

**Перепроверено на `origin/main` — живо:**

| Что | Где | Статус |
|---|---|---|
| 🔴 Транспорт без шифрования | [BackendHttp.cs:75](OrbitalSIP/Services/BackendHttp.cs:75) | Живо. Клиентская часть по-прежнему только предупреждает в лог; закрытие — вне репозитория |
| 🟠 Обновление не перепроверяет состояние звонка | [UpdateService.cs:127](OrbitalSIP/Services/UpdateService.cs:127) | Живо. Проверка `State != CallState.Idle` стоит до `DownloadAndInstallAsync`, повторной перед запуском инсталлятора нет |
| 🟠 `RegisterHotKey` выключен по умолчанию | [SipSettings.cs:67](OrbitalSIP/Services/SipSettings.cs:67) | Живо, `= false`. Осознанное решение, ждёт ручного прогона |
| 🟡 `CleanupMedia` не сбрасывает `IsMuted`/`IsOnHold` | [SipService.cs:897](OrbitalSIP/Services/SipService.cs:897) | Живо. Усиливается новой находкой про несинхронизированные `_muted`/`_onHold` в `ActiveCallView` — чинить надо обе стороны |
| 🟡 `ApplyAudioState` помечает состояние достигнутым в `catch` | [SipService.cs:628](OrbitalSIP/Services/SipService.cs:628) | Живо, `_audioPaused = IsMuted` внутри `catch` |
| ⚪ Корневой `obj/` в индексе, `test.py` | — | Живо |
| ⚪ Avalonia приколочена к 11.0.0 | [OrbitalSIP.csproj:53](OrbitalSIP/OrbitalSIP.csproj:53) | Живо |

**Перепроверено — частично закрыто:**

- ⚪ 5× CS8618 в `GainAudioEndPoint`. Три поля переведены в nullable (`_waveOutEvent?`, `_waveProvider?`, `_waveInEvent?`); `_waveSinkFormat` и `_waveSourceFormat` оставались non-nullable, и чистая сборка печатала **2×** CS8618. ⚠️ В первой редакции здесь было «сборка печатает 0 CS8618» — неверно, цифра снята с инкрементальной сборки. См. «Поправка к первой редакции этого дополнения» ниже.

**Статус неизвестен (не проверял):**

- 🟠 Инсталлятор не подписан ([UpdateService.cs:321](OrbitalSIP/Services/UpdateService.cs:321)) — `build.ps1` и `installer/OrbitalSIP.iss` в этом проходе не открывались.
- Всё, что прошлый обзор перечислил в разделе «Закрыто», кроме пунктов выше — поштучно не перепроверялось.

## Проверено и НЕ является багом

Записано, чтобы следующий проход не поднимал заново.

- **Ручная простановка `Authorization` в `RecentsView`, `OperatorStatsControl`, `StatusService`.** Выглядит как обход `AuthRefreshHandler` с протухшим токеном. Не баг: [AuthRefreshHandler.cs:39](OrbitalSIP/Services/AuthRefreshHandler.cs:39) **перезаписывает** заголовок свежим токеном для любого запроса со схемой `Bearer`.
- **`TaskDialog.ResolveDueDate` на `DateTime.Now`/`DateTime.Today`** ([TaskDialog.axaml.cs:183](OrbitalSIP/Views/TaskDialog.axaml.cs:183)). Оба возвращают `Kind = Local`, поэтому `ToString("o")` печатает смещение — срок уходит на бэкенд однозначным. В отличие от `RecentsView`, здесь всё верно.
- **`SetBusy`/`SetWizardBusy` без `try/finally` в `SurveyDialog`.** Выглядит как «мастер анкеты навсегда блокируется при сбое сети». Не срабатывает: все девять публичных методов `FlowsService` ловят исключения внутри и возвращают `null`/`false`, а пути с `null` сами зовут `SetWizardBusy(false)`.
- **`updateBtn.Click` в настройках без `try/finally`** ([SettingsView.axaml.cs:226](OrbitalSIP/Views/SettingsView.axaml.cs:226)). `CheckAndUpdateAsync` целиком обёрнут в `try/catch/finally`.
- **`StatusService.StartPolling()` без защиты от двойного старта.** `DispatcherTimer.Start()` идемпотентен, а повторный `FetchStateAsync` отсекается флагом `_isFetching`.
- **`SmsComposeDialog` диспозит `_lifetimeCancellation` в `Closed`** ([SmsComposeDialog.axaml.cs:196](OrbitalSIP/Views/SmsComposeDialog.axaml.cs:196)), хотя `SurveyDialog` явно отказался это делать и объяснил почему ([SurveyDialog.axaml.cs:48](OrbitalSIP/Views/SurveyDialog.axaml.cs:48)). Конкретного пути, где `.Token` читается после `Dispose()`, найти не удалось: единственные обращения после закрытия — `IsCancellationRequested`, который после `Dispose()` не бросает. Расхождение между двумя соседними диалогами стоит выровнять, но как баг оно не подтверждено.

## Тесты

439 зелёных, ни одного файла без `Assert`. Что видно по покрытию:

- **`SipService` не покрыт ни одним тестом.** Самый рискованный класс в проекте — машина состояний, три потока SIPSorcery, весь жизненный цикл аудио — не имеет тестового файла вообще. Все находки этого прохода по `SipService` (включая 🔴) нашлись бы юнит-тестом на переходы, если бы шов для него существовал.
- Нет тестов на `UpdateService`, `StatusService`, `ScriptService`, `AuthRefreshHandler`, `BackendAuth`, `HttpErrorNotifier`, атомарность `SipSettings.Save`, и ни на один `View`.
- Вырожденные проверки — см. 🟠 выше про `WaveOutDevicesTests`/`GainAudioEndPointLifecycleTests`.
- `Dispose_IsIdempotent` ([:26](OrbitalSIP.Tests/GainAudioEndPointLifecycleTests.cs:26)) и `CloseThenDispose_DoesNotThrow` ([:56](OrbitalSIP.Tests/GainAudioEndPointLifecycleTests.cs:56)) не содержат `Assert` — это законно для «не бросает», но защищают только от исключения.
- `FlowsServiceTests` — 2 факта на сервис с девятью публичными методами.

## Ручной прогон, которого требуют находки

1. Входящий звонок, нажать «Ответить» и **одновременно** положить трубку со стороны звонящего. Повторить 20 раз, следить за числом открытых winmm-хэндлов процесса и за `app.log` (🔴).
2. Звонок → удержание из мини-виджета → развернуть панель → проверить надпись и цвет кнопки Hold.
3. Активный звонок → кнопка «Клавиатура» → убедиться, что тонального набора нет вообще.
4. Ночная смена (или подмена часового пояса): звонок в 02:00 местного времени, затем «Недавние» — звонка в списке не будет.
5. Настройки открыть/закрыть 50 раз, снять дамп памяти, посчитать живые `BottomNavControl`/`SettingsView`.
6. Отключить USB-гарнитуру → настройки → сохранить → воткнуть обратно → проверить, куда пошёл звук.
7. Набор номера с удержанным Enter — посмотреть на таймер звонка.

---

# Что исправлено (тот же день, ветка `fix/audit-2026-08-20-views`)

Сборка: **0 ошибок, 0 предупреждений** (было 6: 4× NU1903 + 2× CS8618).
Тесты: **484 зелёных** (было 439), из них 45 новых.

Порядок работы: модели, которые можно покрыть чисто, писались тест-первым — модель
сначала создавалась с текущим (сломанным) поведением, тест наблюдался красным на самом
дефекте, и только потом чинился. Проводка, для которой честного красного теста нет
(SIP-колбэки, жизненный цикл Avalonia), помечена ниже отдельно.

## 🔴 Закрыто

| Что | Где | Как |
|---|---|---|
| Отменённый входящий не освобождал аудиоустройства | [SipService.cs:400](OrbitalSIP/Services/SipService.cs:400) | Обработчик `ServerCallCancelled` идёт через `OnCallEnded()`. `_pendingUas` снимается до вызова, чтобы `TryRejectPending` не отправил второй финальный ответ на INVITE, который CANCEL уже завершил. **Теста нет** — до этого пути не дотянуться без живого стека SIPSorcery |

## 🟠 Закрыто

| Что | Где | Как | Тест |
|---|---|---|---|
| Кнопка Hold показывала обратное состоянию | [ActiveCallView.axaml.cs:94](OrbitalSIP/Views/ActiveCallView.axaml.cs:94), [MainWindow.axaml.cs:611](OrbitalSIP/MainWindow.axaml.cs:611) | Конструктор принимает `isMuted`/`isOnHold`; `SetStatus` перерисовывает кнопку, поэтому вид ресинхронизируется на каждом переходе Active/OnHold; добавлен `SipService.SetHold(bool)`, и `MainWindow` просит состояние, а не переключение | Нет (проводка вида) |
| Утечка дерева view на каждую навигацию | [BottomNavControl.axaml.cs](OrbitalSIP/Views/BottomNavControl.axaml.cs), [SettingsView.axaml.cs](OrbitalSIP/Views/SettingsView.axaml.cs) | `OnDetachedFromVisualTree` с отпиской в обоих; лямбда на `UpdateAvailable` вынесена в поле, иначе её нечем отписать. У `BottomNavControl` заодно `OnAttachedToVisualTree` — анимация смены экрана делает detach+attach, и контрол должен пережить его | Нет (проводка вида) |
| UTC-дата как местный рабочий день | [CallHistoryWindow.cs](OrbitalSIP/Models/CallHistoryWindow.cs), [RecentsView.axaml.cs:96](OrbitalSIP/Views/RecentsView.axaml.cs:96) | Границы считаются от местной полуночи и переводятся в UTC | ✔ 11 тестов, тест-первым |
| BYE рвал звонок на всём окне набора | [ByeAuthorization.cs](OrbitalSIP/Models/ByeAuthorization.cs), [SipService.cs:366](OrbitalSIP/Services/SipService.cs:366) | Принимается только BYE, называющий **установленный** диалог. RFC 3261 §15: до установления диалога BYE не определён, отказ от набора — это CANCEL | ✔ 9 тестов, тест-первым |
| Гонка входящего INVITE с `CallAsync` | [SipService.cs:388](OrbitalSIP/Services/SipService.cs:388) | Переход `Idle → IncomingRinging` захватывается под `_lock`; при сбое `AcceptCall` захват возвращается. Занятому INVITE по-прежнему не отвечаем — re-INVITE живого диалога приходит сюда же и принадлежит агенту SIPSorcery | Нет (SIP-колбэк) |
| Двойной Enter = два параллельных логина | [LoginView.axaml.cs:57](OrbitalSIP/Views/LoginView.axaml.cs:57) | `SingleWindowGuard` как флаг single-flight (уже покрыт своими тестами) + `e.Handled = true` | ✔ через существующие `SingleWindowGuardTests` |
| SIPSorcery 10.0.13 с двумя high-уязвимостями | [OrbitalSIP.csproj:58](OrbitalSIP/OrbitalSIP.csproj:58) | 10.0.16, оба NU1903 ушли, API не менялся | Сборка + весь прогон |
| Регресс-тесты на утечку аудио ничего не проверяли | [RequiresPlaybackDeviceFactAttribute.cs](OrbitalSIP.Tests/RequiresPlaybackDeviceFactAttribute.cs), [WaveOutDevicesTests.cs](OrbitalSIP.Tests/WaveOutDevicesTests.cs) | Тесты, которым нужно реальное устройство, скипаются вслух вместо зелёного `false == false`; вырожденные `Count >= 0` заменены на границу, которую реально использует `PlaybackDevice.IsUsable` | ✔ и проверено мутацией — см. ниже |
| Обновление убивало звонок, начавшийся во время загрузки | [UpdateService.cs:296](OrbitalSIP/Services/UpdateService.cs:296) | Состояние перепроверяется непосредственно перед запуском инсталлятора; скачанный файл сохраняется | Нет |

## 🟡 Закрыто

- [OperatorStatsControl.axaml.cs:59](OrbitalSIP/Views/OperatorStatsControl.axaml.cs:59) — `OnAttachedToVisualTree` перезапускает таймер, который убивала входная анимация.
- [AudioDeviceChoice.cs](OrbitalSIP/Models/AudioDeviceChoice.cs) + [SettingsView.axaml.cs:90](OrbitalSIP/Views/SettingsView.axaml.cs:90) — отсутствующее устройство получает собственную строку списка, сохранение её не затирает. ✔ 13 тестов, тест-первым.
- [ActiveCallView.CallInfo.cs:50](OrbitalSIP/Views/ActiveCallView.CallInfo.cs:50) — `_callInfoLoaded` ставится только при непустом ответе.
- [IconFlash.cs](OrbitalSIP/Views/IconFlash.cs) — четыре копии вспышки-галочки заменены одной, которая не может залипнуть; повторный клик во время вспышки игнорируется, а не захватывает галочку как «оригинал».
- [ActiveCallView.axaml.cs:975](OrbitalSIP/Views/ActiveCallView.axaml.cs:975) — `try/finally` вокруг `SubmitTaskAsync`.
- [SipService.cs:735](OrbitalSIP/Services/SipService.cs:735) — `_audioEndPoint` и `_mediaSession` публикуются одним `lock` в конце `TryCreateAudio`; `catch` освобождает то, что успел построить (иначе перенос публикации в конец сам стал бы третьей редакцией утечки winmm).
- [GainAudioEndPoint.cs:290](OrbitalSIP/Services/Audio/GainAudioEndPoint.cs:290) — весь обмен устройства идёт под `_renderLock`, устройство/буфер/формат публикуются вместе; `InitPlaybackDevice` отказывается открывать устройство после `Dispose`/`CloseAudioSink`. Сообщения об ошибке поднимаются уже вне лока — они уходят в UI.
- [SipService.cs:906](OrbitalSIP/Services/SipService.cs:906) — `CleanupMedia` сбрасывает `IsMuted`/`IsOnHold`.
- [SipService.cs:637](OrbitalSIP/Services/SipService.cs:637) — `ApplyAudioState` больше не помечает переход достигнутым внутри `catch`.
- [LoginView.axaml.cs:120](OrbitalSIP/Views/LoginView.axaml.cs:120) и [:147](OrbitalSIP/Views/LoginView.axaml.cs:147) — тела ответов `/api/auth/login` и `/api/auth/sip-credentials` в лог не пишутся. [StatusService.cs:222](OrbitalSIP/Services/StatusService.cs:222) — тело на успешном пути тоже убрано.
- [MainWindow.axaml.cs:497](OrbitalSIP/MainWindow.axaml.cs:497) — `StartOutgoingCall` проверяет `State != Idle`.

## ⚪ Закрыто

- [BreakCountdown.cs](OrbitalSIP/Models/BreakCountdown.cs) — `TotalMinutes` вместо `Minutes`, истёкший перерыв даёт `00:00`, а не отрицательные цифры. ✔ 8 тестов, тест-первым.
- [SipSettings.cs:130](OrbitalSIP/Services/SipSettings.cs:130) — `Flush(flushToDisk: true)` до переименования.
- `"Unmute"/"Mute"` больше не захардкожены; ключи `Mute`, `Unmute`, `AudioDeviceUnavailable` добавлены в `ru.json`.
- 2× CS8618 закрыты: `_waveSinkFormat`/`_waveSourceFormat` стали nullable — заодно проверка `_waveSinkFormat != null` в `GotEncodedMediaFrame`, которая до этого была мёртвой, стала осмысленной.
- Корневой `obj/` (16 файлов) и `test.py` убраны из индекса.
- `if (bottomNav != null) if (bottomNav != null)` в `ExpandedView`.

## Поправка к первой редакции этого дополнения

В разделе «Перепроверено — закрыто» было написано, что 5× CS8618 в `GainAudioEndPoint`
закрыты и «сборка печатает **0** CS8618». **Это неверно.** Цифра была снята с
инкрементальной сборки, которая ничего не компилировала (1,01 с). Чистая сборка
(`--no-incremental`) на `10.0.13` даёт **6** предупреждений: 4× NU1903 и 2× CS8618
(`_waveSinkFormat`, `_waveSourceFormat`). То есть на момент аудита было закрыто три из
пяти, а не пять. Оставшиеся два закрыты этим заходом.

Урок ровно тот же, что и у прошлого прохода: цифру, которая идёт в отчёт как
«перепроверено», надо снимать с чистой сборки.

## Проверка мутацией

Усиленный `RepeatedCreateAndDispose_KeepsOpeningTheDevice` прогнан мутацией: тело
`GainAudioEndPoint.Dispose()` закомментировано, прогон повторён.

**Результат честнее, чем хотелось:** красными стали `Dispose_ClosesThePlaybackDevice` и
`GotAudioSample_AfterDispose_IsANoOp`, а сам цикл на 64 итерации **остался зелёным** —
64 повисших хэндла не исчерпывают драйвер настольной Windows. Комментарий в тесте это
теперь и говорит: цикл — smoke-тест на повторное открытие, а ловят утечку две прямые
проверки. Мутация откачена редактированием файла.

## Осталось открытым

| Severity | Что | Почему не в этом заходе |
|---|---|---|
| 🔴 | Транспорт без шифрования | Вне репозитория: нужен https на бэкенде и TLS/SRTP на SIP |
| 🟠 | DTMF не отправляется (`SendDtmfAsync` без вызывающих) | Отложено сознательно: нужна тональная клавиатура в панели звонка, то есть решение по UI, а не правка |
| 🟠 | Инсталлятор не подписан | Пайплайн сборки; `build.ps1`/`OrbitalSIP.iss` в этом заходе не открывались — **статус неизвестен** |
| 🟠 | `RegisterHotKey` выключен по умолчанию | Осознанно; нужен ручной прогон четырёх хоткеев на живой машине |
| 🟡 | «Сохранить» в настройках кладёт активный звонок | Продуктовое решение: прятать кнопку при активном звонке или спрашивать подтверждение |
| ⚪ | 14 захардкоженных русских строк в `SurveyDialog` | Отдельная задача на i18n |
| ⚪ | 11 ключей (теперь 12) отсутствуют в `kk`/`tg`/`uz` | Нужны переводы, не код. `AudioDeviceUnavailable` добавлен только в `ru.json` — счёт вырос на один |
| ⚪ | Сырой JSON вердикта в `SurveyDialog` | Нужно решить, что оператору вообще показывать |
| ⚪ | `async void` мимо `SafeHandler` в шести видах | Латентно: все сервисы сегодня ловят исключения внутри |
| ⚪ | `ReadLine` без ограничения длины и ACL named pipe | Нужна проверка на многопользовательской машине |
| ⚪ | Avalonia приколочена к 11.0.0 | Апгрейд снимет три обхода багов `AutoCompleteBox` и, возможно, сломает их — планировать отдельно |

## Чего эти правки НЕ доказывают

Ни одна не прогонялась на живом стенде. Сборка и 484 юнит-теста — это всё, что за ними
стоит. Ручной прогон из предыдущего раздела остаётся обязательным, и первым пунктом —
отмена входящего в момент ответа, ради которой всё затевалось. Отдельно: бамп SIPSorcery
проверен только сборкой и тестами, регресс по реальным звонкам на нём не делался.
