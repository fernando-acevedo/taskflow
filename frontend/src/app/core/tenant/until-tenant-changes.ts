import { ofType } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { MonoTypeOperatorFunction, Observable, takeUntil } from 'rxjs';
import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { TenantActions } from '@core/tenant/store/tenant.actions';

/**
 * Drops the answer to a read that is still in flight when the workspace changes or the session ends, so data
 * of the previous tenant (or user) never lands in the stores that were just reset. Put it on the inner
 * observable of a read effect.
 */
export function untilTenantChanges<T>(actions$: Observable<Action>): MonoTypeOperatorFunction<T> {
  return takeUntil(
    actions$.pipe(
      ofType(
        TenantActions.tenantSwitched,
        ShellActions.logoutClicked,
        AuthApiActions.sessionExpired,
      ),
    ),
  );
}
