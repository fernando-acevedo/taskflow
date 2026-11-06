import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { DashboardApiService } from '../dashboard-api.service';
import { DashboardApiActions, DashboardPageActions } from './dashboard.actions';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';

export const loadMyDashboard = createEffect(
  (actions$ = inject(Actions), api = inject(DashboardApiService)) =>
    actions$.pipe(
      ofType(DashboardPageActions.opened),
      switchMap(() =>
        api.mine().pipe(
          untilTenantChanges(actions$),
          map((dashboard) => DashboardApiActions.myDashboardLoaded({ dashboard })),
          catchError((error: unknown) => of(DashboardApiActions.myDashboardLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);
