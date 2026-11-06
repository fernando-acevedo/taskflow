import { HttpClient } from '@angular/common/http';
import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Store } from '@ngrx/store';
import { EMPTY, Subject, concatMap, map, catchError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthApiActions } from '../auth/store/auth.actions';
import { authFeature } from '../auth/store/auth.feature';
import { toApiError } from '../http/api-error';
import { Notifier } from '../notifications/notifier';
import { THEME_STORAGE_KEY, ThemePreference, isThemePreference, readSavedTheme } from './theme';

/**
 * The per-user theme (ADR 0015). The browser's saved value applies before and without a session; when a session
 * starts (login or restore) the server's value wins, and a change made while signed in is also saved on the server.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly store = inject(Store);
  private readonly notifier = inject(Notifier);
  private readonly user = this.store.selectSignal(authFeature.selectUser);
  /** Changes waiting to be saved; concatMap keeps them in order. */
  private readonly saves = new Subject<ThemePreference>();
  private pendingSaves = 0;
  /** The user whose server value was already applied: later emissions of the same user must not re-apply it. */
  private appliedFor: string | null = null;

  readonly preference = signal<ThemePreference>(readSavedTheme());

  constructor() {
    effect(() => {
      const preference = this.preference();
      this.document.documentElement.style.colorScheme = preference === 'System' ? 'light dark' : preference.toLowerCase();
    });

    // When a session starts the server's value replaces the browser's (also one picked on the login screen).
    effect(() => {
      const user = this.user();
      if (!user) {
        this.appliedFor = null;
        return;
      }
      if (user.id === this.appliedFor) {
        return;
      }
      this.appliedFor = user.id;
      if (isThemePreference(user.themePreference)) {
        this.apply(user.themePreference);
      }
    });

    this.saves
      .pipe(
        concatMap((preference) =>
          this.http.put<void>(`${environment.apiUrl}/api/users/me/preferences`, { themePreference: preference }).pipe(
            map(() => {
              this.pendingSaves--;
              this.store.dispatch(AuthApiActions.themePreferenceSaved({ themePreference: preference }));
            }),
            catchError((error: unknown) => {
              this.pendingSaves--;
              this.notifier.error(toApiError(error));
              // Back to the last value the server confirmed, unless a newer change is still on its way.
              const confirmed = this.user()?.themePreference;
              if (this.pendingSaves === 0 && isThemePreference(confirmed)) {
                this.apply(confirmed);
              }
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe();
  }

  use(preference: ThemePreference): void {
    this.apply(preference);
    if (this.user()) {
      this.pendingSaves++;
      this.saves.next(preference);
    }
  }

  private apply(preference: ThemePreference): void {
    this.preference.set(preference);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, preference);
    } catch {
      // Storage can be unavailable (private mode); the choice still applies to this session.
    }
  }
}
