export const THEME_PREFERENCES = ['System', 'Light', 'Dark'] as const;

export type ThemePreference = (typeof THEME_PREFERENCES)[number];

export const DEFAULT_THEME: ThemePreference = 'System';

/** Also hard-coded in the inline script of index.html, which runs before Angular: keep both in sync. */
export const THEME_STORAGE_KEY = 'taskflow.theme';

export function isThemePreference(value: unknown): value is ThemePreference {
  return THEME_PREFERENCES.includes(value as ThemePreference);
}

export function readSavedTheme(): ThemePreference {
  try {
    const saved = localStorage.getItem(THEME_STORAGE_KEY);
    return isThemePreference(saved) ? saved : DEFAULT_THEME;
  } catch {
    return DEFAULT_THEME;
  }
}
