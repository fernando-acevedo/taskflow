import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, filter, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { AuthApiActions } from '../../auth/store/auth.actions';
import { authFeature } from '../../auth/store/auth.feature';
import { DemoService } from '../../demo/demo.service';
import { TenantApiService } from '../tenant-api.service';
import { TenantStorage } from '../tenant-storage';
import { TenantActions, TenantApiActions } from './tenant.actions';

/** After sign-in or session restore: fetch the user's tenants; the reducer picks the remembered one (or the demo's, or the first). */
export const loadTenants = createEffect(
  (
    actions$ = inject(Actions),
    api = inject(TenantApiService),
    storage = inject(TenantStorage),
    demo = inject(DemoService),
  ) =>
    actions$.pipe(
      ofType(AuthApiActions.sessionRestored, AuthApiActions.loginSucceeded),
      switchMap(({ user }) =>
        api.mine().pipe(
          map((tenants) =>
            TenantApiActions.tenantsLoaded({
              tenants,
              preferredId: storage.read(user.id) ?? demo.status().tenantId ?? null,
            }),
          ),
          // Without tenants the app still opens; the API then answers by its own rules.
          catchError(() => of(TenantApiActions.tenantsLoaded({ tenants: [], preferredId: null }))),
        ),
      ),
    ),
  { functional: true },
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
