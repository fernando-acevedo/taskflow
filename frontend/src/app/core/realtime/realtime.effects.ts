import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { tap } from 'rxjs';
import { AuthApiActions, ShellActions } from '../auth/store/auth.actions';
import { TenantActions } from '../tenant/store/tenant.actions';
import { RealtimeService } from './realtime.service';

/** Another session must not receive what this one was watching: cut the connection. */
export const stopRealtime = createEffect(
  (actions$ = inject(Actions), realtime = inject(RealtimeService)) =>
    actions$.pipe(
      ofType(ShellActions.logoutClicked, AuthApiActions.sessionExpired),
      tap(() => realtime.stop()),
    ),
  { functional: true, dispatch: false },
);

/**
 * Another workspace must not receive what this one was watching: cut the connection, but keep the open page's
 * projects, so that if the navigation home is blocked the page shows "Disconnected" instead of a stale "Live".
 */
export const suspendRealtimeOnTenantSwitch = createEffect(
  (actions$ = inject(Actions), realtime = inject(RealtimeService)) =>
    actions$.pipe(
      ofType(TenantActions.tenantSwitched),
      tap(() => realtime.suspend()),
    ),
  { functional: true, dispatch: false },
);
