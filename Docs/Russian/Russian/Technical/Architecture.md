# Архитектура

## Обзор

HyPrism следует архитектурному паттерну **Console + IPC + React SPA**:

```
┌─────────────────────────────────────────────────────┐
│  Консольное приложение .NET  (Program.cs)           │
│  ├── Bootstrapper.cs (DI-контейнер)                 │
│  ├── Services/ (бизнес-логика)                      │
│  └── IpcService.cs (реестр IPC-каналов)             │
│         ↕ Сокетный мост Electron.NET                │
│  ┌─────────────────────────────────────────────┐    │
│  │  Electron Main Process                      │    │
│  │  └── BrowserWindow (без рамки)              │    │
│  │       └── preload.js (contextBridge)        │    │
│  │            ↕ ipcRenderer                    │    │
│  │       ┌─────────────────────────────┐       │    │
│  │       │  React SPA                  │       │    │
│  │       │  ├── App.tsx (маршрутизация)│       │    │
│  │       │  ├── pages/ (представления) │       │    │
│  │       │  ├── components/ (общие)    │       │    │
│  │       │  └── lib/ipc.ts (генерир.)  │       │    │
│  │       └─────────────────────────────┘       │    │
│  └─────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────┘
```

## Процесс запуска

1. `Program.Main()` инициализирует логгер Serilog
2. Устанавливает `ElectronLogInterceptor` на `Console.Out`/`Console.Error`
3. `Bootstrapper.Initialize()` создаёт DI-контейнер
4. `ElectronNetRuntime.RuntimeController.Start()` создаёт процесс Electron
5. `ElectronBootstrap()` создаёт безрамочный `BrowserWindow`, загружающий `file://wwwroot/index.html`
6. `IpcService.RegisterAll()` регистрирует все обработчики IPC-каналов
7. React SPA монтируется, получает данные через типизированные IPC-вызовы

## Модель коммуникации

Вся коммуникация между фронтендом и бэкендом использует **именованные IPC-каналы**:

```
Именование каналов: hyprism:{домен}:{действие}
Примеры:            hyprism:game:launch
                    hyprism:settings:get
                    hyprism:i18n:set
```

### Типы каналов

| Тип | Направление | Паттерн |
|-----|-------------|---------|
| **send** | React → .NET (без ожидания ответа) | `send(channel, data)` |
| **invoke** | React → .NET → React (запрос/ответ) | `invoke(channel, data)` → ожидает `:reply` |
| **event** | .NET → React (push) | `on(channel, callback)` |

### Модель безопасности

- `contextIsolation: true` — рендерер не имеет доступа к Node.js
- `nodeIntegration: false` — нет `require()` в рендерере
- `preload.js` предоставляет только `window.electron.ipcRenderer` через `contextBridge`

## Внедрение зависимостей

Все сервисы регистрируются как синглтоны в `Bootstrapper.cs`:

```csharp
var services = new ServiceCollection();
services.AddSingleton<ConfigService>();
services.AddSingleton<IpcService>();
// ... и так далее
return services.BuildServiceProvider();
```

`IpcService` получает все остальные сервисы через внедрение через конструктор и выступает центральным мостом между React и .NET.

## Перехват логов

Electron.NET выводит неструктурированные сообщения в stdout/stderr (например, `[StartCore]:`, `|| ...`). HyPrism перехватывает их через `ElectronLogInterceptor` (кастомный `TextWriter`, установленный на `Console.Out`/`Console.Error`) и направляет их через структурированный `Logger`:

- Сообщения фреймворка → `Logger.Info("Electron", ...)`
- Отладочные сообщения (`[StartCore]`, `BridgeConnector`) → `Logger.Debug("Electron", ...)`
- Паттерны ошибок (`ERROR:`, `crash`) → `Logger.Warning("Electron", ...)`
- Шумовые паттерны (`GetVSyncParametersIfAvailable`) → подавляются
