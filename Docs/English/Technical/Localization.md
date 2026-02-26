# Localization

HyPrism supports 12 languages with runtime switching.

## Locale Files

**Location:** `Assets/Locales/{code}.json`

**Supported languages:**

| Code | Language |
|------|----------|
| en-US | English |
| ru-RU | Russian |
| de-DE | German |
| es-ES | Spanish |
| fr-FR | French |
| ja-JP | Japanese |
| ko-KR | Korean |
| pt-BR | Portuguese (Brazil) |
| tr-TR | Turkish |
| uk-UA | Ukrainian |
| zh-CN | Chinese (Simplified) |
| be-BY | Belarusian |

## File Format

```json
{
  "_langName": "English",
  "_langCode": "en-US",
  "button": {
    "play": "Play",
    "settings": "Settings"
  },
  "dashboard": {
    "welcome": "Welcome, {0}!"
  }
}
```

**Rules:**
- Nested keys: `dashboard.welcome`
- Placeholders: `{0}`, `{1}`, etc.
- Metadata fields prefixed with `_` (e.g. `_langName`, `_langCode`)

## IPC Channels

| Channel | Type | Description |
|---------|------|-------------|
| `hyprism:i18n:get` | invoke | Get all translations for current language |
| `hyprism:i18n:current` | invoke | Get current language code |
| `hyprism:i18n:set` | invoke | Change language (accepts `{ language: "code" }`) |
| `hyprism:i18n:languages` | invoke | Get list of available languages |

## Backend Usage

```csharp
// Get translation
var text = LocalizationService.Instance.Translate("button.play");

// Change language
_configService.Configuration.Language = "ru-RU";
LocalizationService.Instance.LoadLanguage("ru-RU");
```

## Frontend Usage

```typescript
import { ipc } from '../lib/ipc';

// Get all translations
const translations = await ipc.i18n.get();

// Change language
await ipc.i18n.set({ language: 'ru-RU' });

// Get available languages
const langs = await ipc.i18n.languages();
// → [{ code: 'en-US', name: 'English' }, ...]
```

## `useTranslation()` Hook (`lib/i18n.tsx`)

Inside Preact components use the `useTranslation` hook (also exported as `useI18n`):

```tsx
import { useTranslation } from '../lib/i18n';

export function MyComponent() {
  const { t, language, setLanguage } = useTranslation();

  // Simple key lookup
  const label = t('button.play');

  // Key with named parameters
  const msg = t('dashboard.welcome', { name: 'Player' });

  // Key with a fallback string when the key is missing
  const hint = t('tooltip.help', 'Help');

  return (
    <div>
      <span>{msg}</span>
      <button onClick={() => setLanguage('ru-RU')}>{language}</button>
    </div>
  );
}
```

### Return values

| Value | Type | Description |
|-------|------|-------------|
| `t` | `(key, paramsOrFallback?) => string` | Translate a dot-separated key |
| `language` | `string` | Current language code (e.g. `'en-US'`) |
| `setLanguage` | `(code: string) => void` | Switch language and persist via IPC |
| `i18n` | `{ language, changeLanguage }` | Low-level i18n object (legacy, prefer `setLanguage`) |

### `t()` signature

```ts
t(key: string): string
t(key: string, params: Record<string, string | number>): string  // named {param} substitution
t(key: string, fallback: string): string                         // fallback when key is missing
```
