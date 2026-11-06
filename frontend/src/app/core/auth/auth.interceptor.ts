import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { catchError, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MY_TENANTS_PATH } from '../tenant/tenant-api.service';
import { TENANT_HEADER } from '../tenant/tenant.models';
import { tenantFeature } from '../tenant/store/tenant.feature';
import { AUTH_ENDPOINTS_PREFIX } from './auth-api.service';
import { isTokenRejected } from './auth-errors';
import { AuthApiActions } from './store/auth.actions';
import { TokenRefresher } from './token-refresher';
import { TokenStorage } from './token-storage';

const apiPrefix = `${environment.apiUrl}/api/`;
const authPrefix = `${environment.apiUrl}${AUTH_ENDPOINTS_PREFIX}`;

const myTenantsUrl = `${environment.apiUrl}${MY_TENANTS_PATH}`;

function withTenant(request: HttpRequest<unknown>, tenantId: string | null): HttpRequest<unknown> {
  return tenantId && request.url !== myTenantsUrl
    ? request.clone({ setHeaders: { [TENANT_HEADER]: tenantId } })
    : request;
}

function withToken(
  request: HttpRequest<unknown>,
  accessToken: string | null,
): HttpRequest<unknown> {
  return accessToken
    ? request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })
    : request;
}

/**
 * Adds the access token and the current tenant (`X-Tenant`) to API calls. On a 401 it refreshes the token once (shared by concurrent requests)
 * and retries. Only an API that rejects the refresh token ends the session; when the refresh fails because the
 * API can't be reached (network, cold start), the tokens are kept and the request just fails.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(apiPrefix) || request.url.startsWith(authPrefix)) {
    return next(request);
  }

  const tokens = inject(TokenStorage);
  const refresher = inject(TokenRefresher);
  const store = inject(Store);

  const tenanted = withTenant(request, store.selectSignal(tenantFeature.selectCurrentId)());

  return next(withToken(tenanted, tokens.accessToken())).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !tokens.refreshToken()) {
        return throwError(() => error);
      }

      return refresher.refresh().pipe(
        catchError((refreshError: unknown) => {
          if (isTokenRejected(refreshError)) {
            store.dispatch(AuthApiActions.sessionExpired());
          }
          return throwError(() => refreshError);
        }),
        // The tenant may have changed while the refresh ran; the retry carries the current one.
        switchMap((accessToken) =>
          next(
            withToken(
              withTenant(request, store.selectSignal(tenantFeature.selectCurrentId)()),
              accessToken,
            ),
          ),
        ),
      );
    }),
  );
};
