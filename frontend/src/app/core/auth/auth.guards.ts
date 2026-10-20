import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Store } from '@ngrx/store';
import { map, take } from 'rxjs';
import { authFeature } from './store/auth.feature';

// The session is restored before the first navigation (see session.ts), so these read a settled state.
// They only shape navigation; the API enforces authorization on every request.

export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(Store)
    .select(authFeature.selectIsAuthenticated)
    .pipe(
      take(1),
      map((signedIn) => signedIn || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })),
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

export const adminGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(Store)
    .select(authFeature.selectIsAdmin)
    .pipe(
      take(1),
      map((isAdmin) => isAdmin || router.createUrlTree(['/'])),
    );
};
