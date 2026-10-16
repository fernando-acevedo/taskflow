export const SUPPORTED_LANGUAGES = ['en', 'es'] as const;

export type Language = (typeof SUPPORTED_LANGUAGES)[number];

export const DEFAULT_LANGUAGE: Language = 'en';

export const LANGUAGE_STORAGE_KEY = 'taskflow.language';

export function isSupportedLanguage(value: string | null | undefined): value is Language {
  return SUPPORTED_LANGUAGES.includes(value as Language);
}

/**
 * The saved choice wins; otherwise Spanish for any `es-*` browser language and English for everything else.
 */
export function resolveInitialLanguage(
  savedLanguage: string | null,
  browserLanguages: readonly string[],
): Language {
  if (isSupportedLanguage(savedLanguage)) {
    return savedLanguage;
  }
  return browserLanguages[0]?.toLowerCase().startsWith('es') ? 'es' : DEFAULT_LANGUAGE;
}

export function readSavedLanguage(): string | null {
  try {
    return localStorage.getItem(LANGUAGE_STORAGE_KEY);
  } catch {
    return null;
  }
}
