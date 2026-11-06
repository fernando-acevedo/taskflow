import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, filter, map, merge, of, switchMap, takeUntil, tap, withLatestFrom } from 'rxjs';
import { AuthApiActions, ShellActions } from '../../auth/store/auth.actions';
import { authFeature } from '../../auth/store/auth.feature';
import { DemoService } from '../../demo/demo.service';
import { toApiError } from '../../http/api-error';
import { Notifier } from '../../notifications/notifier';
import { TenantApiService } from '../tenant-api.service';
import { TenantStorage } from '../tenant-storage';
import { TenantActions, TenantApiActions } from './tenant.actions';

/**
 * After sign-in or session restore (and on retry): fetch the user's tenants; the reducer picks the remembered one
 * (or the demo's, or the first). Signing out meanwhile drops the answer. A failure ends in the `error` status,
 * not in an empty list: the shell offers a retry, and the guards let the navigation through.
 */
export const loadTenants = createEffect(
  (
    actions$ = inject(Actions),
    api = inject(TenantApiService),
    storage = inject(TenantStorage),
    demo = inject(DemoService),
    store = inject(Store),
  ) => {
    const signedIn$ = actions$.pipe(
      ofType(AuthApiActions.sessionRestored, AuthApiActions.loginSucceeded),
      map(({ user }) => user),
    );
    const retry$ = actions$.pipe(
      ofType(TenantActions.retryClicked),
      withLatestFrom(store.select(authFeature.selectUser)),
      map(([, user]) => user),
      filter((user) => user !== null),
    );
    return merge(signedIn$, retry$).pipe(
      switchMap((user) =>
        api.mine().pipe(
          map((tenants) =>
            TenantApiActions.tenantsLoaded({
              tenants,
              preferredId: storage.read(user.id) ?? demo.status().tenantId ?? null,
            }),
          ),
          catchError((error: unknown) =>
            of(TenantApiActions.tenantsLoadFailed({ error: toApiError(error) })),
          ),
          takeUntil(actions$.pipe(ofType(ShellActions.logoutClicked, AuthApiActions.sessionExpired))),
        ),
      ),
    );
  },
  { functional: true },
);

export const notifyTenantsLoadFailed = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(TenantApiActions.tenantsLoadFailed),
      tap(() => notifier.warning('tenant.loadFailed')),
    ),
  { functional: true, dispatch: false },
);

export const rememberTenant = createEffect(
  (actions$ = inject(Actions), store = inject(Store), storage = inject(TenantStorage)) =>
    actions$.pipe(
      ofType(TenantActions.tenantSwitched),
      withLatestFrom(store.select(authFeature.selectUser)),
      filter(([, user]) => user !== null),
      tap(([{ tenantId }, user]) => storage.save(user!.id, tenantId)),
    ),
  { functional: true, dispatch: false },
);

/** The feature stores reset on their own; going home makes the pages load the new tenant's data. */
export const goHomeOnSwitch = createEffect(
  (actions$ = inject(Actions), router = inject(Router)) =>
    actions$.pipe(
      ofType(TenantActions.tenantSwitched),
      tap(() => void router.navigateByUrl('/')),
    ),
  { functional: true, dispatch: false },
);
