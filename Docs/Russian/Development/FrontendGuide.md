# Руководство по фронтенду

## Обзор

Фронтенд — это React 19 SPA, собранный с помощью Vite 7 и TypeScript 5.9. Он запускается внутри Electron BrowserWindow, загружаемого из `file://wwwroot/index.html`.

## Стек

| Библиотека | Назначение |
|------------|-----------|
| React 19 | UI-фреймворк |
| TypeScript 5.9 | Типобезопасность |
| Vite 7 | Инструмент сборки |
| TailwindCSS v4 | Утилитарный CSS |
| GSAP 3 + @gsap/react | Анимации |
| Lucide React | Иконки |
| React Router DOM | Клиентская маршрутизация |

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

## Анимации (GSAP)

Переходы между страницами и микровзаимодействия используют GSAP:

```tsx
import { useGSAP } from '@gsap/react';
import gsap from 'gsap';

export function MyPage() {
  const containerRef = useRef<HTMLDivElement>(null);

  useGSAP(() => {
    gsap.from(containerRef.current, {
      opacity: 0, y: 20, duration: 0.5, ease: 'power2.out'
    });
  }, []);

  return <div ref={containerRef}>...</div>;
}
```

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

Все иконки берутся из **Lucide React** — без пользовательских SVG-файлов:

```tsx
import { Settings, Download, Play } from 'lucide-react';

<Settings size={18} style={{ color: 'var(--text-secondary)' }} />
```
