import { inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { catchError, firstValueFrom, map, of, switchMap, tap } from 'rxjs';
import { AuthApiService } from './auth-api.service';
import { isTokenRejected } from './auth-errors';
import { AuthApiActions } from './store/auth.actions';
import { TokenStorage } from './token-storage';

/**
 * Runs before the first navigation: trades a stored refresh token for a new session, so guards already
 * know whether the user is signed in. Tokens are dropped only when the API rejects them, not when it is
 * unreachable (for example during an App Service cold start).
 */
export function restoreSession(): Promise<void> {
  const api = inject(AuthApiService);
  const tokens = inject(TokenStorage);
  const store = inject(Store);

  const refreshToken = tokens.refreshToken();
  if (!refreshToken) {
    store.dispatch(AuthApiActions.noSession());
    return Promise.resolve();
  }

  return firstValueFrom(
    api.refresh(refreshToken).pipe(
      tap((response) => tokens.save(response)),
      switchMap(() => api.me()),
      map((user) => AuthApiActions.sessionRestored({ user })),
      catchError((error: unknown) => {
        if (isTokenRejected(error)) {
          tokens.clear();
        }
        return of(AuthApiActions.noSession());
      }),
    ),
  ).then((action) => store.dispatch(action));
}
