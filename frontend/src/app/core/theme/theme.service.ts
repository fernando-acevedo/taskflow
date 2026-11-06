import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { environment } from '../../../environments/environment';
import { authFeature } from '../auth/store/auth.feature';
import { THEME_STORAGE_KEY, ThemePreference, isThemePreference, readSavedTheme } from './theme';

const DARK_QUERY = '(prefers-color-scheme: dark)';

/**
 * The per-user theme (ADR 0015). The browser's saved value applies before and without a session; once the
 * user is known the server's value wins, and a change made while signed in is also saved on the server.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly user = inject(Store).selectSignal(authFeature.selectUser);
  private readonly media = this.document.defaultView?.matchMedia?.(DARK_QUERY) ?? null;
  private readonly systemDark = signal(this.media?.matches ?? false);

  readonly preference = signal<ThemePreference>(readSavedTheme());
  /** What is actually shown: System resolves to the OS setting. */
  readonly effective = computed<'light' | 'dark'>(() => {
    const preference = this.preference();
    if (preference === 'System') {
      return this.systemDark() ? 'dark' : 'light';
    }
    return preference === 'Dark' ? 'dark' : 'light';
  });

  constructor() {
    this.media?.addEventListener('change', (event) => this.systemDark.set(event.matches));

    effect(() => {
      const preference = this.preference();
      this.document.documentElement.style.colorScheme = preference === 'System' ? 'light dark' : preference.toLowerCase();
    });

    // After login or session restore the server's value replaces the browser's.
    effect(() => {
      const fromServer = this.user()?.themePreference;
      if (isThemePreference(fromServer)) {
        this.preference.set(fromServer);
        this.save(fromServer);
      }
    });
  }

  use(preference: ThemePreference): void {
    this.preference.set(preference);
    this.save(preference);
    if (this.user()) {
      this.http.put<void>(`${environment.apiUrl}/api/users/me/preferences`, { themePreference: preference }).subscribe({
        error: () => undefined, // The browser keeps the choice; the next login restores the server's value.
      });
    }
  }

  private save(preference: ThemePreference): void {
    try {
      localStorage.setItem(THEME_STORAGE_KEY, preference);
    } catch {
      // Storage can be unavailable (private mode); the choice still applies to this session.
    }
  }
}
