# Архитектура

## Обзор

HyPrism следует архитектурному паттерну **Console + IPC + Preact SPA** с использованием [Sciter](https://sciter.com/) в качестве встроенного веб-движка:

```
┌─────────────────────────────────────────────────────┐
│  Консольное приложение .NET  (Program.cs)           │
│  ├── Bootstrapper.cs (DI-контейнер)                 │
│  ├── Services/ (бизнес-логика)                      │
│  └── IpcService.cs (реестр IPC-каналов)             │
│         ↕ SciterIpcBridge (xcall / eval)            │
│  ┌─────────────────────────────────────────────┐    │
│  │  SciterAPIHost (EmptyFlow.SciterAPI)         │    │
│  │  └── Window (нативное окно ОС)              │    │
│  │       └── SciterIpcWindowHandler            │    │
│  │            ↕ xcall / __hyprismReceive       │    │
│  │       ┌─────────────────────────────┐       │    │
│  │       │  Preact SPA                 │       │    │
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
2. `Bootstrapper.Initialize()` создаёт DI-контейнер
3. `SciterBootstrap()` создаёт `SciterAPIHost` (загружает нативную библиотеку Sciter)
4. `host.CreateMainWindow()` открывает нативное окно ОС
5. `bridge.Attach(host, window)` регистрирует обработчик IPC-событий на окне
6. `IpcService.RegisterAll()` регистрирует все обработчики IPC-каналов
7. `host.LoadFile("file://...wwwroot/index.html")` загружает Preact SPA
8. `host.Process()` блокирует выполнение — запускает нативный цикл обработки сообщений до закрытия окна

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
| **send** | Preact → .NET (без ожидания ответа) | `send(channel, data)` |
| **invoke** | Preact → .NET → Preact (запрос/ответ) | `invoke(channel, data)` → ожидает `:reply` |
| **event** | .NET → Preact (push) | `on(channel, callback)` |

### Транспорт: JS → C\#

Фронтенд вызывает C#-бэкенд через механизм `xcall` Sciter:

```typescript
// генерируется ipc.ts — потребитель не вызывает это напрямую
Window.this.xcall('hyprismCall', channel, JSON.stringify(data));
```

`SciterIpcWindowHandler.ScriptMethodCall()` получает вызов и диспетчеризует его зарегистрированному обработчику.

### Транспорт: C\# → JS

Бэкенд отправляет события на фронтенд через вычисление глобальной JavaScript-функции:

```csharp
host.ExecuteWindowEval(window,
    $"typeof __hyprismReceive === 'function' && __hyprismReceive({channelJson},{json})",
    out _);
```

`__hyprismReceive` определяется генерируемым `ipc.ts` и диспетчеризует зарегистрированным слушателям `on()`.

## Внедрение зависимостей

Все сервисы регистрируются как синглтоны в `Bootstrapper.cs`:

```csharp
var services = new ServiceCollection();
services.AddSingleton<SciterIpcBridge>();
services.AddSingleton<ISciterIpcBridge>(sp => sp.GetRequiredService<SciterIpcBridge>());
services.AddSingleton<IpcService>();
// ... и так далее
return services.BuildServiceProvider();
```

`IpcService` получает все остальные сервисы через внедрение через конструктор и выступает центральным мостом между Preact и .NET.
