import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { filter, map, switchMap, take } from 'rxjs';
import { tenantFeature } from '../tenant/store/tenant.feature';
import { authFeature } from './store/auth.feature';

// The session is restored before the first navigation (see session.ts), so these read a settled state.
// They only shape navigation; the API enforces authorization on every request.

/** Emits once the signed-in user's tenants are known (right away when signed out), so the first calls carry a tenant. */
function tenantsSettled() {
  return inject(Store)
    .select(tenantFeature.selectStatus)
    .pipe(
      filter((status) => status !== 'loading'),
      take(1),
    );
}

export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  const store = inject(Store);
  return tenantsSettled().pipe(
    switchMap(() => store.select(authFeature.selectIsAuthenticated)),
    take(1),
    map(
      (signedIn) =>
        signedIn || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }),
    ),
  );
};

export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(Store)
    .select(authFeature.selectIsAuthenticated)
    .pipe(
      take(1),
      map((signedIn) => !signedIn || router.createUrlTree(['/'])),
    );
};

/** The platform's super-admin (Identity role Admin). */
export const adminGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(Store)
    .select(authFeature.selectIsAdmin)
    .pipe(
      take(1),
      map((isAdmin) => isAdmin || router.createUrlTree(['/'])),
    );
};

/** Owner or Admin of the current tenant (the super-admin always passes). */
export const tenantAdminGuard: CanActivateFn = () => {
  const router = inject(Router);
  const store = inject(Store);
  return tenantsSettled().pipe(
    switchMap(() => store.select(tenantFeature.selectCanManage)),
    take(1),
    map((canManage) => canManage || router.createUrlTree(['/'])),
  );
};
