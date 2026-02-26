# Руководство по фронтенду

## Обзор

Фронтенд — это Preact 10 SPA, собранный с помощью Vite и TypeScript. Он запускается внутри нативного окна Sciter, загружаемого из `wwwroot/index.html`.

## Стек

| Библиотека / Модуль | Назначение |
|---------------------|------------|
| Preact 10 | UI-фреймворк |
| TypeScript | Типобезопасность |
| Vite | Инструмент сборки |
| TailwindCSS v3 | Утилитарный CSS |
| `lib/motion.tsx` | Кастомные анимации (безопасен для Sciter, API похож на framer-motion) |
| `components/icons/LucideIcons.tsx` | Встроенные SVG-иконки (Lucide-совместимые, без внешнего пакета) |
| `lib/i18n.tsx` | Провайдер i18n и хук `useTranslation()` |
| Кастомный page-state router | Клиентская навигация через состояние `currentPage` в `App.tsx` |

## Страницы

| Страница | Файл | Маршрут | Описание |
|----------|------|---------|----------|
| Панель управления | `pages/Dashboard.tsx` | `/` | Запуск игры, прогресс, статус |
| Новости | `pages/News.tsx` | `/news` | Лента новостей Hytale |
| Настройки | `pages/Settings.tsx` | `/settings` | Настройки приложения |
| Менеджер модов | `pages/ModManager.tsx` | `/mods` | Просмотр и управление модами |

## Компоненты

| Компонент | Файл | Описание |
|-----------|------|----------|
| TitleBar | `components/TitleBar.tsx` | Заголовок безрамочного окна с кнопками управления |
| Sidebar | `components/Sidebar.tsx` | Боковая панель навигации с иконками |
| GlassCard | `components/GlassCard.tsx` | Обёртка карточки в стиле glass-morphism |

## UI-примитивы

Чтобы интерфейс оставался единообразным и поддерживаемым, используйте общие примитивы из `Frontend/src/components/ui/`:

- **PageContainer** (`components/ui/PageContainer.tsx`) — единая максимальная ширина, центрирование и адаптивные отступы для основных страниц
- **SettingsHeader** (`components/ui/SettingsHeader.tsx`) — унифицированный заголовок секции/страницы (title + опциональное описание, опциональный слот для действий)
- **SelectionCard** (`components/ui/SelectionCard.tsx`) — переиспользуемая карточка-выбор с вариантами оформления, слотом иконки, состоянием selected и обработчиком клика

### Общие Controls (единый источник правды)

Для большинства интерактивных элементов (кнопки, иконки-кнопки, таб-переключатели, скролл-области и lightbox) используйте:

- **Controls** (`components/ui/Controls.tsx`) — стабильный barrel-export (реализации лежат в `components/ui/controls/`)

Этот файл специально централизует внешний вид/поведение UI, чтобы не плодить множество одноразовых стилей кнопок.

**Что использовать**
- `Button` — базовая кнопка для большинства действий
- `IconButton` — квадратные действия только с иконкой (refresh/copy/export и т.д.)
- `LinkButton` — кнопка в стиле ссылки для текстовых действий (без одноразовых `className` кнопок)
- `LauncherActionButton` — градиентные основные действия (Play/Stop/Download/Update/Select) с "лаунчерным" шрифтом/весом
- `SegmentedControl` — переключатели в стиле табов с бегунком (как в Instances)
- `AccentSegmentedControl` — обёртка над `SegmentedControl`, автоматически применяющая текущий accent-стиль (используется для фильтров в Logs и табов Instances)
- `Switch` — accent-reactive переключатель
- `ScrollArea` — единообразный overflow + опциональный `thin-scrollbar`
- `ImageLightbox` — просмотр скриншотов по центру с навигацией `1/3 < >`
- `DropdownTriggerButton` — стандартный триггер dropdown (label + chevron + состояние open)
- `MenuActionButton` — полноширинные пункты меню для hover-меню (например, Worlds overlay)
- `MenuItemButton` — полноширинные пункты для context/popover меню (заменяет одноразовые `button className="..."` в меню)
- `ModalFooterActions` — стандартная строка действий в футере модалки (отступы + граница + фон)

**Размеры IconButton**
- Используйте `IconButton size="sm" | "md" | "lg"` вместо ручных `h-/w-` классов.

**Варианты IconButton**
- Используйте `variant="overlay"` для навигации в скриншотах/lightbox (без glass hover).

**Правило**
- Если хочется написать новую кнопку с кастомным `className="...rounded...hover..."` — лучше использовать `Button`/`IconButton` из `@/components/ui/Controls`.

**Правки controls**
- Если нужно поменять конкретный элемент, правьте роль-модуль в `components/ui/controls/`, а `Controls.tsx` держите как тонкий re-export.

## Разделение больших страниц

Если файл страницы становится слишком большим, выносите связные блоки UI в отдельную папку рядом со страницей и переносите общие типы туда же.

- Пример: `InstancesPage` использует `Frontend/src/pages/instances/` для вынесенных компонентов и общих типов.

## Создание компонента

```tsx
import { ipc } from '../lib/ipc';
import type { Profile } from '../lib/ipc';

interface Props {
  profileId: string;
}

export function ProfileCard({ profileId }: Props) {
  const [profile, setProfile] = useState<Profile | null>(null);

  useEffect(() => {
    ipc.profile.get().then(setProfile);
  }, [profileId]);

  if (!profile) return <div>Loading...</div>;
  return (
    <div className="p-4 rounded-xl" style={{ backgroundColor: 'var(--bg-light)' }}>
      {profile.name}
    </div>
  );
}
```

## Темизация

Все цвета темы определены как CSS custom properties в `Frontend/src/index.css`:

```css
:root {
  --bg-darkest: #0D0D10;    /* Фон приложения */
  --bg-dark: #14141A;        /* Боковая панель, заголовок */
  --bg-medium: #1C1C26;      /* Карточки */
  --bg-light: #252533;       /* Приподнятые поверхности */
  --bg-lighter: #2E2E40;     /* Границы, полоса прокрутки */
  --text-primary: #F0F0F5;   /* Основной текст */
  --text-secondary: #A0A0B8; /* Второстепенный текст */
  --text-muted: #6B6B80;     /* Приглушённый / неактивный */
  --accent: #7C5CFC;         /* Основной акцент (фиолетовый) */
  --accent-hover: #6A4AE8;   /* Акцент при наведении */
  --success: #4ADE80;
  --warning: #FBBF24;
  --error: #F87171;
}
```

**Использование:**
- Классы Tailwind для компоновки/отступов: `className="flex items-center gap-2 p-4"`
- CSS-переменные для цветов темы: `style={{ color: 'var(--accent)' }}`
- Никогда не используйте захардкоженные hex-цвета — всегда CSS-переменные

## Анимации (`lib/motion`)

Анимации используют кастомный модуль `lib/motion.tsx` — лёгкую замену framer-motion, безопасную для Sciter. GSAP **не используется**.

```tsx
import { motion, AnimatePresence } from '../lib/motion';

// Анимация при монтировании
<motion.div
  initial={{ opacity: 0, y: 20 }}
  animate={{ opacity: 1, y: 0 }}
  exit={{ opacity: 0, y: -10 }}
  transition={{ duration: 0.25, ease: 'easeOut' }}
>
  ...
</motion.div>

// Именованные варианты
<motion.div
  variants={{ hidden: { opacity: 0 }, visible: { opacity: 1 } }}
  initial="hidden"
  animate="visible"
/>

// Анимация выхода
<AnimatePresence mode="wait">
  {show && <motion.div key="panel" exit={{ opacity: 0 }}>…</motion.div>}
</AnimatePresence>
```

Доступные компоненты: `motion.div`, `motion.span`, `motion.button`.
Поддерживаются все CSS-анимируемые свойства в camelCase.

## Использование IPC

Весь IPC доступен через автогенерируемый объект `ipc`:

```typescript
import { ipc } from '../lib/ipc';

// Invoke (запрос/ответ)
const settings = await ipc.settings.get();

// Send (без ожидания ответа)
ipc.windowCtl.minimize();

// Подписка на события
ipc.game.onProgress((data) => setProgress(data.progress));

// Открыть URL
ipc.browser.open('https://example.com');
```

## Провайдеры контекста

Состояние игры управляется через `GameContext`:

```tsx
const { isPlaying, launch, cancel } = useGame();
```

Добавляйте новые контексты в `Frontend/src/contexts/` для состояния других доменов.

## Иконки

Все иконки встроены в `components/icons/LucideIcons.tsx` как Preact-компоненты — внешний пакет `lucide-react` **не установлен**.

```tsx
import { Settings, Download, Play, type LucideIcon } from '../components/icons/LucideIcons';

<Settings size={18} color="var(--text-secondary)" />
```

Чтобы добавить новую иконку, скопируйте SVG-пути с [lucide.dev](https://lucide.dev) в `LucideIcons.tsx` по аналогии с существующими.

## Навигация / Маршрутизация

В приложении используется **кастомный page-state router** без `react-router-dom`.

Страницы рендерятся на основе состояния `currentPage` в `App.tsx`. Для программной навигации отправьте кастомное событие:

```ts
window.dispatchEvent(new CustomEvent('hyprism:menu:navigate', { detail: { page: 'settings' } }));
```

Допустимые значения страниц определены типом `PageType` в `App.tsx`.

## Совместимость с Sciter

Приложение работает внутри Sciter (QuickJS), поэтому некоторые браузерные API отсутствуют или ведут себя иначе. Используйте следующие утилиты.

### Буфер обмена — `utils/clipboard.ts`

```ts
import { copyToClipboard } from '../utils/clipboard';

await copyToClipboard('hello'); // возвращает true при успехе
```

Если `navigator.clipboard` недоступен (Sciter), используется `document.execCommand('copy')`.

**Всегда используйте эту функцию вместо прямого `navigator.clipboard.writeText`.**

### Аудио — хелперы в `MusicPlayer`

Sciter не реализует все методы `HTMLAudioElement`. В `components/layout/MusicPlayer.tsx` каждый вызов аудио обёрнут в guard-функции:

```ts
audioPlay(audio)             // безопасный audio.play()
audioPause(audio)            // безопасный audio.pause()
audioSetVolume(audio, vol)   // безопасный audio.volume =
audioGetVolume(audio)        // безопасное чтение audio.volume
audioIsPaused(audio)         // безопасное чтение audio.paused
```

Не вызывайте `audio.play()` / `audio.pause()` / `audio.volume` напрямую — используйте эти хелперы в любом коде, связанном с аудио.
