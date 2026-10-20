import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AUTH_ENDPOINTS_PREFIX } from './auth-api.service';
import { AuthApiActions } from './store/auth.actions';
import { TokenRefresher } from './token-refresher';
import { TokenStorage } from './token-storage';

const apiPrefix = `${environment.apiUrl}/api/`;
const authPrefix = `${environment.apiUrl}${AUTH_ENDPOINTS_PREFIX}`;

function withToken(request: HttpRequest<unknown>, accessToken: string | null): HttpRequest<unknown> {
  return accessToken ? request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } }) : request;
}

/**
 * Adds the access token to API calls. On a 401 it refreshes the token once (shared by concurrent requests)
 * and retries; if the refresh fails too, the session is over and the user is sent to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(apiPrefix) || request.url.startsWith(authPrefix)) {
    return next(request);
  }

  const tokens = inject(TokenStorage);
  const refresher = inject(TokenRefresher);
  const store = inject(Store);

  return next(withToken(request, tokens.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !tokens.refreshToken()) {
        return throwError(() => error);
      }

      return refresher.refresh().pipe(
        catchError((refreshError: unknown) => {
          store.dispatch(AuthApiActions.sessionExpired());
          return throwError(() => refreshError);
        }),
        switchMap((accessToken) => next(withToken(request, accessToken))),
      );
    }),
  );
};
