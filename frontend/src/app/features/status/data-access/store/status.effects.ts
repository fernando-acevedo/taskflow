import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { StatusApiService } from '../status-api.service';
import { StatusApiActions, StatusPageActions } from './status.actions';

export const loadStatus = createEffect(
  (actions$ = inject(Actions), api = inject(StatusApiService)) =>
    actions$.pipe(
      ofType(StatusPageActions.statusRequested),
      switchMap(() =>
        forkJoin({ health: api.getHealth(), info: api.getInfo() }).pipe(
          map(({ health, info }) =>
            StatusApiActions.statusLoaded({ health, info, checkedAt: new Date().toISOString() }),
          ),
          catchError((error: unknown) =>
            of(
              StatusApiActions.statusLoadFailed({
                error: error instanceof HttpErrorResponse ? `HTTP ${error.status}` : 'Unknown error',
                checkedAt: new Date().toISOString(),
              }),
            ),
          ),
        ),
      ),
    ),
  { functional: true },
);
