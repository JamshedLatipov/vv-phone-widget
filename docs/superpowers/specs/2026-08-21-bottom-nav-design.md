# Логика нижнего меню

**Дата:** 2026-08-21
**Статус:** утверждён

## Цель

`BottomNavControl` — единственный элемент, присутствующий на всех рабочих
экранах виджета, — не имеет ни одного владельца. Каждый экран подписывается на
свой набор его событий, каждый сам решает, какой таб подсветить, и каждый делает
это по-своему. Результат: из «Настроек» нельзя попасть в «Историю», с экрана
звонка нельзя попасть в набор, кнопка «Контакты» не делает вообще ничего, а
подсветка активного таба на экране звонка врёт.

Сделать навигацию выводимой из одного места: контрол докладывает нажатие,
`MainWindow` решает, куда идти, и сам же выдаёт контролу состояние. Занять
мёртвый четвёртый слот экраном задач оператора. Добавить бейджи, которые
показывают, что виджет требует внимания, когда он свёрнут в другой таб.

## Текущее состояние

Замеры по коду на момент написания:

| Что | Где |
|---|---|
| Кнопка `ContactsBtn` поднимает `OnContactsRequested`, на которое не подписан ни один экран. Экрана контактов не существует | `Views/BottomNavControl.axaml:13` |
| `SettingsView` подписан только на `OnDialerRequested` — из настроек недостижимы «История» и четвёртый таб | `Views/SettingsView.axaml.cs:327-332` |
| `ActiveCallView` не вызывает `SetActiveTab` вообще — во время звонка подсвечен таб, на котором оператор не находится | `Views/ActiveCallView.axaml.cs:331-340` |
| `ShowDialer()` во время звонка подменяет набор экраном звонка, поэтому таб «Набор» молча означает «вернуться в звонок» | `MainWindow.axaml.cs:430-444` |
| `KeypadBtn` на экране звонка поднимает `OnKeypadRequested`, который заведён на `ShowDialer()`, а тот во время звонка возвращает тот же экран звонка. Кнопка пересобирает саму себя, DTMF набрать нельзя | `MainWindow.axaml.cs:679`, `Views/ActiveCallView.axaml.cs:268-270` |
| `SetActiveTab` вручную правит `Opacity` и создаёт `SolidColorBrush` для 4 кнопок и 4 иконок | `Views/BottomNavControl.axaml.cs:100-121` |
| Ни подписей, ни тултипов — табы опознаются только по иконкам | `Views/BottomNavControl.axaml` |

## Границы

В объём входят:

- перевод `BottomNavControl` на одно событие `TabSelected` и свойства состояния;
- единая точка привязки навигации в `MainWindow` (`AttachNav` / `NavigateTo`);
- удаление навигационных событий из `ExpandedView`, `RecentsView`,
  `SettingsView`, `ActiveCallView`;
- замена мёртвого таба «Контакты» на «Задачи»;
- экран `TasksView` со списком задач оператора и отметкой «выполнено»;
- расширение `TaskService` тремя методами и развязка дублирующегося HTTP-кода;
- `NavBadgeService`: один опрос на процесс, бейджи «Задачи» и «История»;
- перевод `OperatorStatsControl` на этот же опрос вместо собственного таймера;
- стили табов в XAML вместо перекраски из code-behind;
- состояние «идёт звонок» на табе «Набор»;
- тултипы и строки локализации во всех четырёх языках;
- починка `KeypadBtn` на экране звонка.

В объём не входят:

- любые изменения бэкенда `../crm_mono` — все нужные эндпоинты уже есть;
- создание задачи с экрана задач: оно требует контекста звонка (`callLogId`) и
  остаётся в `ActiveCallView`;
- детали задачи, комментарии, история изменений — это CRM, а не панель 320px;
- экран контактов;
- подписи под иконками табов;
- перенос `OperatorStatsControl` из `ExpandedView` в отдельный таб.

## Решения

### Контрол не знает, куда ведут его кнопки

`BottomNavControl` теряет четыре события `OnXxxRequested`, `WireButtons` и
`SetActiveTab`. Вместо них — одно событие о нажатии и свойства, которыми его
состоянием управляют снаружи:

```csharp
public event EventHandler<NavTab>? TabSelected;

public NavTab ActiveTab { get; set; }              // сеттер сам переставляет класс
public void SetBadge(NavTab tab, int count, bool alert);
public void SetInCall(bool inCall);
public void SetLoginMode(bool loginMode);
public void ShowUpdateDot(bool visible);           // остаётся как есть
```

Новый `OrbitalSIP/Models/NavTab.cs`:

```csharp
public enum NavTab { Dialer, Recents, Tasks, Settings }
```

Сетка остаётся четырёхколоночной: `Contacts` не удаляется, а заменяется на
`Tasks` (иконка `FormatListChecks`).

### Навигация привязывается в одной точке

`MainWindow` получает `AttachNav`, который вызывается из обоих путей вставки
контента — `SetMainContent` и `CompleteAnimatedContentSwap`:

```csharp
private NavTab _currentTab = NavTab.Dialer;
private bool   _settingsFromLogin;

private void AttachNav(object? content)
{
    var nav = (content as Control)?.FindDescendantOfType<BottomNavControl>();
    if (nav == null) return;             // Widget / Login / Incoming — меню нет

    nav.TabSelected += OnNavTabSelected;
    nav.ActiveTab = _currentTab;
    nav.SetInCall(App.SipService.State is CallState.Active or CallState.OnHold);
    nav.SetLoginMode(_settingsFromLogin);
    _navBadges.ApplyTo(nav);
}

private void NavigateTo(NavTab tab)
{
    if (_settingsFromLogin) { ShowLogin(); return; }
    _currentTab = tab;
    switch (tab)
    {
        case NavTab.Dialer:   ShowDialer();   break;
        case NavTab.Recents:  ShowRecents();  break;
        case NavTab.Tasks:    ShowTasks();    break;
        case NavTab.Settings: ShowSettings(); break;
    }
}
```

Экран, добавленный позже, получает работающую навигацию просто оттого, что
содержит `BottomNavControl`. Забыть подписку становится нельзя.

Утечки это не создаёт: ссылка идёт от контрола к `MainWindow`, а контрол умирает
вместе со своим экраном. Обратной ссылки, как у статического
`App.Updater.UpdateAvailable`, не возникает, поэтому пара
`OnAttachedToVisualTree` / `OnDetachedFromVisualTree` в `BottomNavControl`
новыми подписками не обрастает.

### Режим «настройки открыты до логина»

`ShowSettings` вызывается не только из навигации, но и из `LoginView`. В этом
случае сессии нет, и «Набор», «История», «Задачи» вести некуда.

`_settingsFromLogin` включает `SetLoginMode(true)`: «История» и «Задачи»
переходят в `:disabled`, иконка «Набора» меняется на `ArrowLeft`. `NavigateTo`
в этом режиме на любой таб возвращает на экран логина — то же, что делало
удаляемое `SettingsView.OnBackRequested`.

Флаг обязан сбрасываться при любом уходе с логина (`OnLoginSuccess`,
`ShowLoginAfterSessionExpiry`), иначе навигация залипнет в этом режиме на всю
сессию.

### Состояние «идёт звонок»

`ShowActiveCallView` и `ShowActiveCallWidgetView` ставят `_currentTab =
NavTab.Dialer`. При `SetInCall(true)` таб «Набор» меняет иконку `Dialpad` на
`PhoneInTalk` и красится в `#22C55E`. Смысл «нажми — вернёшься в звонок»
становится видимым вместо подразумеваемого, а `ShowDialer()` перестаёт лгать
подсветкой.

Пульсация — только когда оператор ушёл с экрана звонка:

```csharp
public static class NavPulse
{
    public static bool ShouldPulse(bool inCall, NavTab currentTab) =>
        inCall && currentTab != NavTab.Dialer;
}
```

Причина ограничения — та же, что записана в `Services/WidgetPulse.cs:8-13`:
постоянная анимация на прозрачном topmost-окне держит перерисовку без повода.
На самом экране звонка привлекать внимание к кнопке «вернуться в звонок» нечем.

### `KeypadBtn` перестаёт ходить через навигацию

`ActiveCallView.OnKeypadRequested` больше не заведён на `ShowDialer()`. Кнопка
разворачивает панель DTMF внутри самого `ActiveCallView`, что она и должна была
делать: набор тонов идёт в текущий звонок, а не открывает экран набора номера.

Событие `OnKeypadRequested` и его подписка в `MainWindow` удаляются.

## Экран задач

### Что берётся с бэкенда

Все три эндпоинта уже существуют, изменений в `../crm_mono` не требуется.

| Запрос | Ответ |
|---|---|
| `GET /api/tasks?assignedToId={sub}&status=&page=1&limit=50` | `{ data: Task[], total }`, сортировка `createdAt DESC`, подтянуты `taskType`, `contact`, `lead` |
| `GET /api/tasks/stats?assigneeId={sub}` | `{ total, pending, inProgress, done, overdue }` |
| `PATCH /api/tasks/{id}` | обновлённая задача |

`assignedToId` берётся из `DecodedToken.Sub` тем же `int.TryParse`, что уже
стоит в `Views/ActiveCallView.axaml.cs:966`. Если `sub` не разбирается в число,
список пустой и в лог уходит строка — как там.

### Что означает «открытая задача»

Две особенности бэкенда, которые UI обязан повторить, иначе цифра на бейдже
разойдётся со списком под ним
(`apps/back/src/app/modules/tasks/task.service.ts:581-597`):

1. `status=pending` — это catch-all для незавершённого **за вычетом**
   `in_progress`: условие `NOT IN ('in_progress', 'done', 'completed')`, плюс
   `NULL`. Задачи в работе в эту выборку не попадают.
2. `status=overdue` — вычисляемое «не `done`/`completed` и `dueDate < NOW()`».
   Оно **пересекается** и с `pending`, и с `in_progress`.

Отсюда единое определение на весь виджет:

```
открытые = pending + inProgress          (непересекающиеся, складываются)
просроченные = overdue                   (подмножество открытых, не слагаемое)
```

Следствие для списка: чип «Открытые» делает **два** запроса —
`status=pending` и `status=in_progress` — и склеивает результат, пересортировав
по `createdAt DESC`. Один запрос `status=pending` показал бы меньше задач, чем
обещает бейдж. Чип «Все» — один запрос без параметра `status`.

### Модели

Дополнение к `OrbitalSIP/Models/TaskModels.cs`:

```csharp
public class TaskItem
{
    public int Id; public string Title; public string? Description;
    public string? Status; public string? Priority;
    public DateTimeOffset? DueDate;
    public TaskTypeItem? TaskType;
}

public class TaskStats { public int Total, Pending, InProgress, Done, Overdue; }
public class TaskListResponse { public List<TaskItem> Data; public int Total; }
```

### `TaskService`

Сейчас каждый из двух методов сам достаёт настройки, собирает URL, вешает
Bearer, ловит исключение и логирует. Пять методов такого дублирования не
переживут, поэтому общая часть выносится:

```csharp
private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null,
                                    CancellationToken ct = default);
```

Хелпер централизует ранний выход при отсутствии `BackendUrl` или токена,
логирование и вызов `HttpErrorNotifier`. `CreateTaskAsync` и `GetTaskTypesAsync`
переписываются на него без изменения своих сигнатур.

Добавляются:

```csharp
public Task<TaskListResponse?> GetMyTasksAsync(string? status, CancellationToken ct = default);
public Task<TaskStats?>        GetMyStatsAsync(CancellationToken ct = default);
public Task<bool>              SetStatusAsync(int taskId, string status);

/// <summary>Взведён после первого 403: ability роли оператора не выдана.</summary>
public bool TasksForbidden { get; private set; }
```

`TasksForbidden` — то, чем 403 отличается от «бэкенд не ответил». `TasksView`
по нему показывает «Нет доступа к задачам» вместо «Задач нет», а
`NavBadgeService` по нему навсегда снимает опрос задач. Возврат `null` из
методов означает всё остальное — сеть, 5xx, неразобранное тело.

`GetMyTasksAsync` принимает один статус; склейку `pending` + `in_progress` для
чипа «Открытые» делает вызывающий `TasksView`.

### `TasksView`

Оболочка повторяет `RecentsView`: `Border Width="320"` → `Grid
RowDefinitions="Auto,Auto,*,Auto"` → `TopBarControl` / заголовок с кнопкой
обновления / `ScrollViewer` с `ItemsControl` / `BottomNavControl`.

Строка задачи:

```
│▌ Перезвонить Иванову            [✓]│
│  Обратный звонок · сегодня 16:30    │
```

- слева полоса 3px по приоритету: `urgent` `#EF4444`, `high` `#F59E0B`,
  `medium` `#60A5FA`, `low` `#64748B`;
- заголовок, ниже — имя типа задачи и срок; просроченный срок красным;
- справа одна кнопка «✓» — `PATCH status=done`. Строка убирается оптимистично;
  при неуспехе возвращается на место, а текст ошибки показывается тем же
  приёмом, что `SmsComposeErrorLabel` в `RecentsView`.

Над списком два чипа-фильтра: «Открытые» (по умолчанию — склейка `pending` и
`in_progress`, см. выше) и «Все» (один запрос без параметра `status`).

Пустой список — строка «Задач нет». Ответ 403 — строка «Нет доступа к задачам»
вместо списка.

### `TaskItemPresenter` и `TaskItemViewModel`

Вся вычислимая логика строки уходит в статический `TaskItemPresenter` по
образцу существующих `CallInfoPresenter` и `LeadCallPanelPresenter` — она
покрывается тестами без Avalonia:

```csharp
public static bool   IsOverdue(TaskItem t, DateTimeOffset now);
public static string PriorityColor(string? priority);
public static string DueText(DateTimeOffset? due, DateTimeOffset now);
public static string DueColor(TaskItem t, DateTimeOffset now);
```

`ViewModels/TaskItemViewModel.cs` по образцу `CdrItemViewModel` выставляет
готовые к биндингу свойства, чтобы в XAML не появились конвертеры.

## Бейджи

### Один опрос на процесс

Таймер не может жить внутри `BottomNavControl`: контрол пересоздаётся при каждой
навигации, и опрос перезапускался бы на каждое нажатие таба.

Этот класс ошибок в репозитории уже собран дважды и оба раза задокументирован —
`Views/OperatorStatsControl.axaml.cs:50-58` (анимация смены экрана глушила
двухминутный refresh через ~280 мс, цифры замерзали на всё время, пока панель
открыта) и `Views/BottomNavControl.axaml.cs:36-48` (подписка на статическое
событие пришпиливала дерево контролов).

Поэтому опрос владеет собой сам: `Services/NavBadgeService.cs`, один
`DispatcherTimer`, время жизни — сессия, а не экран.

```csharp
public sealed class NavBadgeService : IDisposable
{
    public int OpenTasks    { get; private set; }   // pending + inProgress
    public int OverdueTasks { get; private set; }
    public int NewMissed    { get; private set; }

    public event Action? Changed;

    public void Start();
    public void Stop();
    public Task RefreshNowAsync();
    public void MarkRecentsSeen();
    public void ApplyTo(BottomNavControl nav);
}
```

Сервис отвечает только за расписание опроса, HTTP и уведомление подписчиков.
Вся арифметика — сложение открытых, watermark пропущенных, решение о цвете и
тексте пилюли — вынесена в чистый `Models/NavBadgeState.cs`, который и покрыт
тестами.

`AttachNav` вызывает `ApplyTo` — свежепостроенный контрол получает последние
известные числа. `MainWindow` подписан на `Changed` и перерисовывает текущий
нав.

`Start()` вызывается после успешного логина, `Stop()` — при выходе и при
истёкшей сессии. `RefreshNowAsync()` — после отметки задачи выполненной и после
завершения звонка.

### Источники

| Бейдж | Запрос | Интервал |
|---|---|---|
| Задачи | `GET /api/tasks/stats?assigneeId={sub}` → `pending + inProgress`, отдельно `overdue` | 2 мин |
| История | `GET /api/contact-center/operators/{username}/details?range=today` → `Stats.MissedCalls` | 2 мин |

Второй адрес и его период — те же, что уже использует
`OperatorStatsControl.LoadStatsAsync` (`Views/OperatorStatsControl.axaml.cs:92`).
Дублировать запрос не будем: `NavBadgeService` становится единственным
владельцем опроса, а `OperatorStatsControl` подписывается на его результат и
теряет собственный таймер вместе с парой
`OnAttachedToVisualTree`/`OnDetachedFromVisualTree`, заведённой ради него.
Следствие — один HTTP-запрос вместо двух и исчезновение асимметрии, из-за
которой панель статистики замерзала.

### Watermark для пропущенных

Бэкенд отдаёт число пропущенных **за смену**, а не «непрочитанных», поэтому:

```
NewMissed = max(0, MissedCalls - _seenMissed)
```

`MarkRecentsSeen()` при переходе в таб «История» ставит `_seenMissed =
MissedCalls`. Хранение в памяти: смена примерно равна сессии, переживать
рестарт незачем. Уменьшение `MissedCalls` (смена перевалила за полночь и
счётчик за «сегодня» сбросился) не должно давать отрицательное значение —
отсюда `max(0, …)`.

Задачам watermark не нужен: там счётчик и так означает «сколько висит».

### Поведение при отказах

Интервал растёт через существующий `PollBackoff.Next(failures, healthy: 2 мин,
max: 10 мин)`.

Два правила против баннер-спама — ровно та боль, что описана в
`Models/PollBackoff.cs:7-12`:

1. Фоновый опрос **не вызывает** `HttpErrorNotifier`, только `AppLogger`. Бейдж
   не тот повод, ради которого оператору показывают баннер посреди разговора.
2. Ответ 403 на `tasks/stats` выключает опрос задач до конца сессии, одной
   строкой в лог. Бейдж скрывается. Если ability роли оператора не выдана,
   виджет не должен ходить туда каждые две минуты всю смену.

При недоступном бэкенде бейджи держат последнее известное значение, а не
обнуляются: ноль соврал бы сильнее, чем устаревшее число.

### Отрисовка

`SetBadge(NavTab tab, int count, bool alert)`:

- пилюля в правом верхнем углу иконки, там же, где сейчас `UpdateDot`;
- `count == 0` — скрыта, `count > 9` — текст «9+»;
- `alert: true` (есть просроченные) — фон `#EF4444`, иначе `#3B82F6`;
- зелёная точка обновления на «Настройках» остаётся отдельным элементом: она не
  число и с бейджем не конфликтует.

## Стили

Перекраска из code-behind заменяется классами стилей в `<UserControl.Styles>`
самого `BottomNavControl.axaml`:

```xml
<Style Selector="Button.nav-tab">
  <Setter Property="Opacity" Value="0.65" />
</Style>
<Style Selector="Button.nav-tab.active">
  <Setter Property="Opacity" Value="1.0" />
</Style>
<Style Selector="Button.nav-tab materialIcons|MaterialIcon">
  <Setter Property="Foreground" Value="#8AA0B8" />
</Style>
<Style Selector="Button.nav-tab.active materialIcons|MaterialIcon">
  <Setter Property="Foreground" Value="#60A5FA" />
</Style>
<Style Selector="Button.nav-tab.in-call materialIcons|MaterialIcon">
  <Setter Property="Foreground" Value="#22C55E" />
</Style>
<Style Selector="Button.nav-tab:pointerover">
  <Setter Property="Opacity" Value="0.85" />
</Style>
<Style Selector="Button.nav-tab:disabled">
  <Setter Property="Opacity" Value="0.28" />
</Style>
```

Сеттер `ActiveTab` сводится к перестановке одного класса:

```csharp
foreach (var (tab, btn) in _buttons)
    btn.Classes.Set("active", tab == value);
```

Реакции на наведение курсора у табов сейчас нет вообще — селектор
`:pointerover` её добавляет.

Отдельной ветки под масштабирование не нужно: виджет масштабируется
`LayoutTransformControl` вокруг всего содержимого
(`MainWindow.axaml.cs:736-739`), поэтому `Height="46"` и размеры бейджей
тянутся сами.

## Локализация

Тултипы `ToolTip.Tip="{i18n:I18n …}"` на всех четырёх табах. Тултип «Набора» во
время звонка подменяется на `InCall` («Идёт разговор») — иначе единственное, что
объясняет сменившуюся иконку, это цвет. Подписи под иконками не добавляются: при
высоте полосы 46px и четырёх табах на 320px текст либо обрежется, либо
потребует раздуть панель.

Новые ключи во все четыре файла `Assets/i18n/{ru,uz,kk,tg}.json`: `Tasks`,
`Recents`, `Settings`, `TasksOpen`, `TasksAll`, `TasksEmpty`, `TasksNoAccess`,
`TaskDone`, `TaskOverdue`, `InCall`. Ключи `Dialer` и `Task` уже есть.

## Изменения в коде

### Новые файлы

- `OrbitalSIP/Models/NavTab.cs`
- `OrbitalSIP/Models/NavBadgeState.cs` — чистая арифметика бейджей
- `OrbitalSIP/Services/NavPulse.cs`
- `OrbitalSIP/Services/NavBadgeService.cs`
- `OrbitalSIP/Services/TaskItemPresenter.cs`
- `OrbitalSIP/ViewModels/TaskItemViewModel.cs`
- `OrbitalSIP/Views/TasksView.axaml` + `.axaml.cs`

### Изменяемые файлы

| Файл | Что |
|---|---|
| `Views/BottomNavControl.axaml` | `ContactsBtn` → `TasksBtn`, бейджи, стили, тултипы |
| `Views/BottomNavControl.axaml.cs` | одно событие вместо четырёх, свойства состояния, удаление `WireButtons` и `SetActiveTab` |
| `MainWindow.axaml.cs` | `AttachNav`, `NavigateTo`, `ShowTasks`, `_currentTab`, `_settingsFromLogin`, владение `NavBadgeService`, вычистка навигационных подписок |
| `Views/ExpandedView.axaml.cs` | удаление `OnSettingsRequested`, `OnRecentsRequested` и их проводки |
| `Views/RecentsView.axaml.cs` | удаление `OnSettingsRequested`, `OnDialerRequested` и их проводки |
| `Views/SettingsView.axaml.cs` | удаление `OnBackRequested` и его проводки |
| `Views/ActiveCallView.axaml.cs` | удаление `OnSettingsRequested`, `OnRecentsRequested`, `OnKeypadRequested`; DTMF-панель по месту |
| `Views/OperatorStatsControl.axaml.cs` | снятие собственного таймера, подписка на `NavBadgeService` |
| `Services/TaskService.cs` | хелпер `SendAsync`, три новых метода |
| `Models/TaskModels.cs` | `TaskItem`, `TaskStats`, `TaskListResponse` |
| `Assets/i18n/*.json` | новые ключи, четыре языка |

## Тесты

Проект покрывает чистую логику, вынесенную в `Models/` и `*Presenter`; UI не
тестируется. Тот же принцип здесь.

| Файл | Что проверяем |
|---|---|
| `NavBadgeStateTests` | `NewMissed = max(0, missed - seen)`; `MarkSeen` гасит бейдж; уменьшение `missed` не даёт отрицательное |
| `NavBadgeStateTests` | `OpenTasks = pending + inProgress`; `overdue` не прибавляется к сумме, а только взводит `alert` |
| `NavBadgeStateTests` | форматирование: `0` — скрыт, `9` — «9», `10` — «9+»; `alert` только при `overdue > 0` |
| `NavPulseTests` | пульс только при `inCall && currentTab != Dialer` |
| `TaskItemPresenterTests` | `IsOverdue`: граница «ровно сейчас», статусы `done`/`completed` не просрочены, `dueDate == null`; цвет приоритета, включая неизвестное значение; текст срока |
| `TaskServiceTests` | разбор `{ data, total }`; 403 взводит `TasksForbidden` и отличим от 500, который его не взводит; пустое тело ответа не роняет |

`AttachNav`, `NavigateTo` и отрисовка бейджа — код UI, проверяются прогоном
приложения.

## Порядок работ

1. `NavTab`, переписанный `BottomNavControl` (стили, бейджи, in-call,
   login-режим).
2. `AttachNav` / `NavigateTo` в `MainWindow`, вычистка событий из четырёх
   экранов.
3. `TaskService`: `SendAsync` и три метода; модели; `TaskItemPresenter`.
4. `TasksView`, `TaskItemViewModel`, ключи локализации.
5. `NavBadgeService`, перевод `OperatorStatsControl` на него.
6. Починка `KeypadBtn`.
7. Тесты.

Шаги 1–2 самодостаточны и уже чинят навигацию: на них можно остановиться и
посмотреть результат до того, как начнётся экран задач.

## Риски

**Ability `tasks:read` и `tasks:update` могут быть не выданы роли оператора.**
Виджет сегодня успешно использует `tasks:create`, но это отдельная ability
(`apps/back/src/app/modules/tasks/task.controller.ts:37,48,193`). Проверяется
только на живом бэкенде. Деградация описана выше: таб остаётся, показывает «Нет
доступа к задачам», фоновый опрос выключается.

**`_settingsFromLogin` может залипнуть.** Флаг должен сбрасываться на каждом
пути ухода с логина, иначе навигация до конца сессии будет отправлять любой таб
обратно на экран логина.

**`CompleteAnimatedContentSwap` — второй путь вставки контента.** Если забыть в
нём `AttachNav`, после анимированной смены экрана нав окажется без
обработчиков. Это ровно тот баг, который дизайн призван исключить, поэтому обе
точки правятся одним изменением и обе должны быть проверены прогоном.

**`OperatorStatsControl` теряет собственный опрос.** Если `NavBadgeService` не
запущен (не было логина, вызван `Stop()`), панель статистики останется пустой.
Она обязана переживать это пустыми значениями, а не исключением.
