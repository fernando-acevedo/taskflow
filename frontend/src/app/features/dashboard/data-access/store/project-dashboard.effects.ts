import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, map, of, switchMap } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { DashboardApiService } from '../dashboard-api.service';
import { DashboardApiActions, ProjectOverviewActions } from './dashboard.actions';

export const loadProjectDashboard = createEffect(
  (actions$ = inject(Actions), api = inject(DashboardApiService)) =>
    actions$.pipe(
      ofType(ProjectOverviewActions.opened),
      switchMap(({ projectId }) =>
        api.project(projectId).pipe(
          map((dashboard) => DashboardApiActions.projectDashboardLoaded({ dashboard })),
          catchError((error: unknown) =>
            of(DashboardApiActions.projectDashboardLoadFailed({ projectId, error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);
