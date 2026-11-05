import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { LabelsApiService } from '../labels-api.service';
import { LabelsApiActions, LabelsPageActions } from './labels.actions';
import { labelsFeature } from './labels.feature';

export const loadLabels = createEffect(
  (actions$ = inject(Actions), api = inject(LabelsApiService)) =>
    actions$.pipe(
      ofType(LabelsPageActions.opened),
      switchMap(({ projectId }) =>
        api.list(projectId).pipe(
          map((labels) => LabelsApiActions.labelsLoaded({ projectId, labels })),
          catchError((error: unknown) => of(LabelsApiActions.labelsLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

// Writes run one after another with concatMap: none is dropped, and each one's outcome reaches its form.

export const createLabel = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(LabelsApiService)) =>
    actions$.pipe(
      ofType(LabelsPageActions.createSubmitted),
      withLatestFrom(store.select(labelsFeature.selectProjectId)),
      concatMap(([{ label, submissionId }, projectId]) =>
        projectId === null
          ? of(LabelsApiActions.labelSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.create(projectId, label).pipe(
              map((created) => LabelsApiActions.labelSaved({ label: created, message: 'labels.created', submissionId })),
              catchError((error: unknown) => of(LabelsApiActions.labelSaveFailed({ error: toApiError(error), submissionId }))),
            ),
      ),
    ),
  { functional: true },
);

export const updateLabel = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(LabelsApiService)) =>
    actions$.pipe(
      ofType(LabelsPageActions.updateSubmitted),
      withLatestFrom(store.select(labelsFeature.selectProjectId)),
      concatMap(([{ labelId, changes, submissionId }, projectId]) =>
        projectId === null
          ? of(LabelsApiActions.labelSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.update(projectId, labelId, changes).pipe(
              map((saved) => LabelsApiActions.labelSaved({ label: saved, message: 'labels.saved', submissionId })),
              catchError((error: unknown) => of(LabelsApiActions.labelSaveFailed({ error: toApiError(error), submissionId }))),
            ),
      ),
    ),
  { functional: true },
);

export const deleteLabel = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(LabelsApiService)) =>
    actions$.pipe(
      ofType(LabelsPageActions.deleteConfirmed),
      withLatestFrom(store.select(labelsFeature.selectProjectId)),
      concatMap(([{ labelId }, projectId]) =>
        projectId === null
          ? of(LabelsApiActions.labelSaveFailed({ error: { code: 'unexpected', fields: {} } }))
          : api.delete(projectId, labelId).pipe(
              map(() => LabelsApiActions.labelDeleted({ labelId })),
              catchError((error: unknown) => of(LabelsApiActions.labelSaveFailed({ error: toApiError(error) }))),
            ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(LabelsApiActions.labelSaved, LabelsApiActions.labelDeleted, LabelsApiActions.labelSaveFailed),
      tap((action) => {
        switch (action.type) {
          case LabelsApiActions.labelSaved.type:
            notifier.success(action.message);
            break;
          case LabelsApiActions.labelDeleted.type:
            notifier.success('labels.deleted');
            break;
          default:
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
