# Генерация IPC-кода

IPC-мост HyPrism на 100% автоматически генерируется из C#-аннотаций. Никаких вручную написанных TypeScript IPC-типов.

## Принцип работы

1. Разработчик добавляет аннотации `@ipc` и `@type` в XML-комментариях C# в `IpcService.cs`
2. `Scripts/generate-ipc.mjs` парсит эти аннотации
3. Генератор создаёт единый самодостаточный файл `Frontend/src/lib/ipc.ts`
4. MSBuild запускает это автоматически перед каждой сборкой фронтенда

## @ipc — Определение IPC-каналов

```csharp
/// @ipc invoke hyprism:settings:get -> SettingsSnapshot
```

Типы: invoke (запрос/ответ), send (без ожидания ответа), event (push-уведомление из .NET)

## @type — Определение TypeScript-интерфейсов

```csharp
/// @type SettingsSnapshot { language: string; musicEnabled: boolean; }
```

## Конфликты имён доменов

| IPC-домен | Имя экспорта |
|-----------|-------------|
| window | ipc.windowCtl |
| console | ipc.consoleCtl |

## Интеграция с MSBuild

Цель `GenerateIpcTs` запускается автоматически при изменении `IpcService.cs`.

## Добавление нового канала

1. Добавьте обработчик и аннотацию `@ipc` в `IpcService.cs`
2. Добавьте `@type` (при необходимости) в XML-комментарий класса
3. Запустите `node Scripts/generate-ipc.mjs` или `dotnet build`
4. Используйте в React: `const result = await ipc.myDomain.myAction();`
