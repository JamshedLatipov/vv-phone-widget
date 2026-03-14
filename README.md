# Техническое задание: десктопный VoIP-софтфон "Orbital SIP" (.NET / Avalonia)

## 1. Цель продукта

Разработать легковесное нативное десктопное приложение для аудио-звонков по протоколу SIP с интерфейсом в формате "widget-first".

Приложение должно:

- работать как плавающий виджет поверх других окон на Windows
- быстро раскрываться в компактный интерфейс звонка
- инициировать и принимать SIP-вызовы через безопасные транспортные протоколы (WSS/TLS/TCP/UDP)
- иметь минимальные накладные расходы по памяти и запускаться как нативный процесс

## 2. Целевые платформы и ограничения

- **Основная платформа MVP:** Windows 10/11
- **Возможное расширение:** macOS и Linux (после стабилизации Windows-версии)
- **Форм-фактор:** плавающий виджет (FAB) + компактное окно звонка

## 3. Технологический стек (обновлённый)

- **UI:** Avalonia UI (XAML, кроссплатформенный)
- **Runtime:** .NET 8/9 (с опциональным Native AOT для релизных сборок)
- **VoIP engine:** SIPSorcery (C#)
- **Media:** SIPSorceryMedia.Windows (WindowsAudioEndPoint / DirectSound / WASAPI опции)
- **Хранилище данных:** SQLite через Entity Framework Core
- **Secure storage:** Windows DPAPI (Data Protection API) для паролей/токенов
- **Глобальные хоткеи:** SharpHook (или Win32-API для минимальной зависимости)
- **Notifications:** Microsoft.Toolkit.Uwp.Notifications для Windows Toast

### Зафиксированные технологические решения

- Использовать Avalonia вместо WebView и браузерных движков
- SIPSorcery вместо SIP.js (полностью управляемая C#-библиотека)
- Прямой доступ к аудиодевайсам через SIPSorceryMedia.Windows для минимальной задержки

## 4. Основные пользовательские сценарии (как раньше)

Сценарии сохранены: исходящий вызов, входящий вызов, работа во время активного вызова, восстановление после потери сети, смена статуса оператора.

Дополнительно:

- Глобальные хоткеи и Tray позволяют управлять вызовами, не разворачивая UI
- Secure storage хранит SIP-пароль с использованием DPAPI, пароли не попадают в логи

## 5. Функциональные требования (адаптированы под .NET)

### 5.1. Режимы отображения (UI States) — реализация в Avalonia

- Состояние `Widget`:
  - `WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost=true`
  - Поддержка перетаскивания через `PointerPressed` -> `BeginMoveDrag(e)`
  - Прилипание к краям экрана по `Screens.Primary.WorkingArea`
  - Визуальная индикация статуса регистрации (бордер/эффект)
  - Сохранение позиции в SQLite/конфигурации

- Состояние `Expanded` / `Active Call`:
  - Плавные переходы размеров и видимости через Avalonia Transitions/Animations
  - Кастомная отрисовка теней и эффектов (BoxShadow-подобные стили)

### 5.2. Телефония (SIPSorcery)

- Поддерживаемые транспорты: UDP/TCP/TLS, WSS (WebSocket Secure) опционально
- Регистрация: `SIPRegistrationUserAgent` (SIPSorcery)
- Медиа: поддержка Opus, PCMA (G.711-alaw), PCMU (G.711-ulaw)
- Аудио: использование `WindowsAudioEndPoint` (или WASAPI/DirectSound) через `SIPSorceryMedia.Windows`

Управление звонком:

- Mute: отключение локального захвата микрофона на уровне аудио-пайплайна
- Hold: модификация SDP (a=sendonly или адрес 0.0.0.0) при удержании
- Transfer: отправка SIP REFER для blind transfer

### 5.3. Статусы оператора и таймеры

- Таймеры на базе `System.Timers.Timer` для высокой точности
- Фоновая служба внутри приложения автоматически возвращает статус `online` после таймера

## 6. Системные требования и интеграции

- Always on Top: свойство окна `Topmost="True"`
- Tray Integration: `TrayIcon` в Avalonia или нативный трэй-API на Windows
- Hotkeys: `SharpHook` или нативное перехватывание клавиш
- Notifications: `Microsoft.Toolkit.Uwp.Notifications` (Toast)

## 7. Этапы разработки (SIPSorcery & Avalonia)

### Этап 1: UI-база (Avalonia)

- Scaffold проекта (.NET/Avalonia)
- Реализация прозрачного круглого виджета (FAB), drag & snap, сохранение позиции
- XAML-шаблоны для `Widget`, `Expanded`, `Incoming`, `Active Call`
- Анимации переходов через Styles/Transitions

### Этап 2: SIP-ядро (SIPSorcery)

- Инициализация `SIPTransport` и каналов (UDP/TCP/TLS)
- Настройка `SIPUserAgent` и `SIPRegistrationUserAgent`
- Обработка событий: REGISTER, INVITE, BYE, REFER
- Логирование SIP-трафика в файл (маскирование паролей)

### Этап 3: Медиа-подсистема

- Интеграция `SIPSorceryMedia.Windows` и `WindowsAudioEndPoint`
- Настройка кодеков (Opus, G.711), выбор устройств ввода/вывода
- Реализация mute/hold без разрыва сессии

### Этап 4: Интеграция и трей

- TrayIcon с контекстным меню (статусы, открыть/свернуть, выход)
- Toast-уведомления при входящем/пропущенном вызове
- Secure storage паролей через DPAPI

### Этап 5: Оптимизация и выпуск

- Native AOT сборка релиза (опционально) для уменьшения времени старта
- Профилирование памяти и CPU
- Acceptance tests по критериям задержки и потребления памяти

## 8. Критерии приемки (обновлённые)

- Приложение запускается как native Windows process (.exe)
- Потребление памяти в idle не выше 100–180 МБ (зависит от сборки и AOT)
- Виджет не имеет визуальных артефактов при изменениях размеров
- Входящий/исходящий звонок устанавливается с задержкой аудио < 150–200 мс
- Таймеры статусов корректно работают при блокировке экрана и в фоне

## 9. Риски и меры снижения

- Риск: AV/антивирусы могут реагировать на захват микрофона — подпись исполняемого файла и белый список
- Риск: Проблемы с конкретными аудиодрайверами — поддержка нескольких audio backends (WASAPI/DirectSound)

## 10. Дополнительно — заметки для реализации

- Логи SIP должны маскировать авторизационные данные
- Для тестирования задержки аудио добавить эндпоинт измерения RTT и аудио loopback
- В релизных сборках включить минимизацию зависимостей и рассмотреть Native AOT

---

## 11. Сборка и запуск

### Требования

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 версии 1809 (17763) или новее
- (Опционально) [Inno Setup 6](https://jrsoftware.org/isdownload.php) — для создания установщика

### Запуск в режиме разработки

```powershell
dotnet run --project OrbitalSIP\OrbitalSIP.csproj
```

### Публикация (self-contained EXE)

```powershell
dotnet publish OrbitalSIP\OrbitalSIP.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\win-x64
```

Результат — одиночный файл `publish\win-x64\OrbitalSIP.exe`, не требующий установленного .NET на целевой машине.

### Создание установщика

```powershell
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer\OrbitalSIP.iss
```

Готовый установщик появится в папке `dist\OrbitalSIP-Setup-1.1.exe`.

> **Примечание:** перед запуском Inno Setup необходимо выполнить публикацию (шаг выше), иначе компилятор не найдёт файлы в `publish\win-x64`.

---

Файл обновлён под стек `.NET 8/9 + Avalonia + SIPSorcery`. Для продолжения я могу:

- сгенерировать базовую структуру проекта (уже начато в папке `OrbitalSIP`),
- добавить XAML-макет Expanded/Incoming states и анимации,
- подключить стартовый код `SipService` для SIPSorcery и примеры регистрации.

Что выполнить дальше? 
