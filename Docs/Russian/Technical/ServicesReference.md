# Справочник сервисов

Все сервисы регистрируются как синглтоны в `Bootstrapper.cs` и внедряются через конструктор.

## Основные сервисы (`Services/Core/`)

### IpcService
- **Файл:** `Services/Core/IpcService.cs`
- **Назначение:** Центральный реестр IPC-каналов — единый источник истины для всего взаимодействия React ↔ .NET
- **Ключевой метод:** `RegisterAll()` — регистрирует все IPC-обработчики
- **Аннотации:** Содержит XML-комментарии `@type` и `@ipc`, используемые генератором кода
- **Домены:** config, game, news, profile, settings, i18n, window, browser, mods, console
- **Таймаут выбора папки:** Для `hyprism:file:browseFolder` используется увеличенный таймаут (300 сек.), чтобы выбор директории не обрывался на фронтенде.
- **Резолв целевого экземпляра модов:** обработчики mod IPC выбирают путь по метаданным реально установленного экземпляра (включая latest) и не используют неявный fallback в плейсхолдер `branch/latest`.
- **Точное нацеливание модов:** mod IPC принимает опциональный `instanceId`; если он передан, он имеет приоритет над branch/version и исключает коллизии при нескольких экземплярах с одной версией.
- **Нацеливание instance-операций:** обработчики удаления инстанса и работы с сейвами принимают `instanceId` и в первую очередь резолвят путь по GUID; branch/version сохранён как fallback для обратной совместимости.
- **Обновление иконок инстанса:** `hyprism:instance:getIcon` возвращает URL файла с cache-busting параметром (`?v=<lastWriteTicks>`), чтобы новый логотип отображался сразу после перезаписи.
- **Правило загрузки иконок на фронтенде:** в списке инстансов запросы иконок выполняются последовательно (не параллельно), чтобы исключить смешивание ответов в общем IPC reply-канале.

### ConfigService
- **Файл:** `Services/Core/ConfigService.cs`
- **Тип:** Singleton
- **Назначение:** Конфигурация приложения (сохраняется в JSON)
- **Пути конфигурации:**
  - Windows: `%APPDATA%/HyPrism/config.json`
  - Linux: `~/.config/HyPrism/config.json`
  - macOS: `~/Library/Application Support/HyPrism/config.json`

### Logger
- **Файл:** `Services/Core/Logger.cs`
- **Тип:** Статический класс
- **Назначение:** Структурированное логирование (бэкенд Serilog + цветной вывод в консоль + буфер в памяти)
- **Методы:** `Info()`, `Success()`, `Warning()`, `Error()`, `Debug()`, `Progress()`
- **Файлы логов:** `{appDir}/Logs/{timestamp}.log`

### LocalizationService
- **Файл:** `Services/Core/LocalizationService.cs`
- **Тип:** Singleton (паттерн Instance)
- **Назначение:** Переключение языков в реальном времени с поддержкой вложенных ключей
- **Файлы локалей:** `Assets/Locales/{code}.json`

### BrowserService
- **Файл:** `Services/Core/BrowserService.cs`
- **Назначение:** Открытие URL в системном браузере по умолчанию

### DiscordService
- **Файл:** `Services/Core/DiscordService.cs`
- **Назначение:** Интеграция с Discord Rich Presence

### GitHubService
- **Файл:** `Services/Core/GitHubService.cs`
- **Назначение:** Проверка релизов и функция самообновления

## Игровые сервисы (`Services/Game/`)

### GameSessionService
- **Назначение:** Управление жизненным циклом игры — загрузка, установка, патчинг, запуск
- **Состояния:** preparing → download → install → patching → launching → running → stopped
- **Режим кастомной авторизации:** Для неофициальных профилей используется патч клиентского бинарника + runtime-агент DualAuth.
- **Политика server JAR:** Лаунчер больше не переписывает `Server/HytaleServer.jar` при запуске с кастомным auth-доменом.
- **Восстановление при 403 official CDN:** если официальный URL возвращает HTTP 403 (просроченный подписанный `verify`-токен), сервис принудительно обновляет кэш версий и один раз повторяет загрузку с official перед fallback на mirror.
- **Синхронизация запуска с выбранным инстансом:** при выборе инстанса обновляются и `SelectedInstanceId`, и legacy-поля запуска (`VersionType`/`SelectedVersion`), а запуск с Dashboard отправляет явные `branch/version`, чтобы исключить старт «старого» инстанса.
- **Приоритет пути запуска:** если задан `SelectedInstanceId`, `GameSessionService` резолвит путь установки/запуска сначала по ID инстанса (без fallback на другой установленный инстанс того же branch/version).
- **Хранение метаданных latest:** `latest.json` хранится в корне ветки (`Instances/<branch>/latest.json`), а не в `Instances/<branch>/latest/latest.json`, чтобы не провоцировать случайное создание плейсхолдер-папок `latest`.
- **Linux NVIDIA EGL fix:** в режиме dedicated launcher экспортирует `__EGL_VENDOR_LIBRARY_FILENAMES` в обнаруженный NVIDIA GLVND vendor JSON (например, `/usr/share/glvnd/egl_vendor.d/10_nvidia.json`), чтобы исключить fallback в `llvmpipe` на затронутых системах.

### ClientPatcher ⚠️
- **Файл:** `Services/Game/ClientPatcher.cs`
- **КРИТИЧЕСКИЙ:** Бинарные манипуляции для целостности игры
- **Правило:** НИКОГДА не изменяйте без явных инструкций

### ModService
- **Назначение:** Просмотр, поиск и управление модами (интеграция с CurseForge)

## Пользовательские сервисы (`Services/User/`)

### ProfileService
- **Назначение:** CRUD-операции с профилями игроков
- **Возможности:** Несколько профилей, управление аватарами, переключение профилей
- **Политика хранения модов:** переключение профиля не перенаправляет `UserData/Mods` в `Profiles/.../Mods`; моды остаются в папке выбранного экземпляра.
- **Формат папок профилей:** профили хранятся в `Profiles/{profileId}` (GUID).
- **Миграция legacy-формата:** при старте лаунчер пытается мигрировать старые name-based папки в `Profiles/` в ID-based формат (best-effort, неразрушающее объединение при наличии обеих папок).
- **Маршрутизация auth для official-профиля:** при переключении на официальный профиль домен аутентификации автоматически устанавливается в `sessionserver.hytale.com`.
