# Структура проекта

```
HyPrism/
├── Program.cs                  # Точка входа: Console → Electron bootstrap
├── Bootstrapper.cs             # Настройка DI-контейнера
├── HyPrism.csproj              # Файл проекта с конвейером MSBuild
├── HyPrism.sln                 # Файл решения
│
├── Frontend/                   # React SPA (Vite + TypeScript)
│   ├── src/
│   │   ├── components/         # Переиспользуемые React-компоненты
│   │   │   ├── modals/         # Модальные диалоги (Settings, OnboardingModal и др.)
│   │   │   ├── ProfileEditor.tsx  # UI управления профилем
│   │   │   └── ...
│   │   ├── pages/              # Компоненты страниц маршрутизации
│   │   │   ├── DashboardPage.tsx  # Главная панель (запуск игры)
│   │   │   ├── NewsPage.tsx       # Страница ленты новостей
│   │   │   ├── InstancesPage.tsx  # Менеджер экземпляров игры
│   │   │   └── ModsPage.tsx       # Браузер/менеджер модов
│   │   ├── contexts/           # Провайдеры React Context
│   │   │   └── AccentColorContext.tsx  # Акцентный цвет темы
│   │   ├── lib/                # Утилиты
│   │   │   └── ipc.ts          # АВТОГЕНЕРИРУЕМЫЙ IPC-мост (не редактировать)
│   │   ├── assets/             # Статические ресурсы фронтенда
│   │   │   ├── locales/        # JSON-файлы локализации (12 языков)
│   │   │   ├── images/         # Изображения и иконки
│   │   │   └── backgrounds/    # Фоны панели управления
│   │   ├── App.tsx             # Корневой компонент с маршрутизацией
│   │   ├── main.tsx            # Точка входа React
│   │   └── index.css           # Глобальные стили + Tailwind
│   ├── index.html              # Входной HTML для Vite
│   ├── vite.config.ts          # Конфигурация Vite (Tailwind, base: './')
│   ├── tsconfig*.json          # Конфигурации TypeScript
│   └── package.json            # Зависимости фронтенда
│
├── Services/                   # Слой сервисов .NET
│   ├── Core/                   # Инфраструктурные сервисы
│   │   ├── App/                # Сервисы приложения (Config, Settings, Update)
│   │   ├── Infrastructure/     # Logger, ConfigService, LocalizationService
│   │   ├── Integration/        # Внешние интеграции (Discord RPC)
│   │   ├── Ipc/                # IpcService - Центральный реестр IPC-каналов
│   │   └── Platform/           # Платформозависимые утилиты
│   ├── Game/                   # Сервисы игровой логики
│   │   ├── Instance/           # Управление экземплярами (InstanceService)
│   │   ├── Launch/             # Запуск игры (GameSessionService)
│   │   ├── Download/           # Управление загрузками
│   │   ├── Mod/                # Управление модами
│   │   ├── Auth/               # Аутентификация Hytale
│   │   ├── Butler/             # Инструмент патчинга Butler
│   │   └── Version/            # Управление версиями
│   └── User/                   # Сервисы, связанные с пользователем
│       ├── ProfileService.cs   # Профили игроков (ник, UUID)
│       ├── SkinService.cs      # Управление скинами и резервное копирование
│       └── HytaleAuthService.cs # Аутентификация аккаунта Hytale
│
├── Models/                     # Модели данных (POCO)
│   ├── Config.cs               # Модель конфигурации
│   ├── Profile.cs              # Модель профиля игрока
│   ├── InstanceMeta.cs         # Метаданные экземпляра
│   └── ...
│
├── Scripts/                    # Скрипты сборки и утилиты
│   ├── generate-ipc.mjs        # IPC codegen: C#-аннотации → ipc.ts
│   └── publish.sh              # Скрипт сборки
│
├── Properties/                 # Метаданные сборки/упаковки и платформенные ресурсы
│   ├── linux/                  # Linux metainfo + метаданные flatpak
│   ├── macos/                  # macOS Info.plist и ресурсы
│   └── windows/                # Иконки/ресурсы Windows
│
├── Docs/                       # Документация
│   ├── English/                # Английская документация
│   └── Russian/                # Русская документация
│
└── wwwroot/                    # Скомпилированный фронтенд (генерируется при сборке)
    ├── index.html              # Продакшен точка входа
    └── assets/                 # Скомпилированные JS/CSS бандлы
```

## Важные замечания

- **`Frontend/src/lib/ipc.ts`** автоматически генерируется скриптом `Scripts/generate-ipc.mjs` — никогда не редактируйте вручную
- **`Frontend/src/assets/`** содержит все статические ресурсы фронтенда (изображения, локализации, фоны)
- **`wwwroot/`** генерируется во время сборки — не редактируйте вручную
- **Папки экземпляров** хранятся как `{branch}/{guid}` (например, `release/abc123-...`)
- **Папки-плейсхолдеры** (например, `latest/` без бинарников клиента) игнорируются при обнаружении экземпляров и не превращаются в реальные экземпляры автоматически
