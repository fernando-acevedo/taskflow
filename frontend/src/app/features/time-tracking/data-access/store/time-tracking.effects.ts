import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, from, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { readBlobError } from '../../../../shared/data-access/blob-error';
import { downloadBlob } from '../../../../shared/data-access/download';
import { TimeTrackingApiService } from '../time-tracking-api.service';
import { TimeEntriesApiActions, TimeEntriesPageActions, TimeReportApiActions, TimeReportPageActions } from './time-tracking.actions';
import { timeTrackingFeature } from './time-tracking.feature';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';

export const loadTaskTime = createEffect(
  (actions$ = inject(Actions), api = inject(TimeTrackingApiService)) =>
    actions$.pipe(
      ofType(TimeEntriesPageActions.opened),
      switchMap(({ projectId, taskId }) =>
        api.list(projectId, taskId).pipe(
          untilTenantChanges(actions$),
          map((time) => TimeEntriesApiActions.taskTimeLoaded({ taskId, time })),
          catchError((error: unknown) => of(TimeEntriesApiActions.taskTimeLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** The open task's project and id, or null when none is open. */
const openTask = (state: { projectId: string | null; taskId: string | null }) =>
  state.projectId !== null && state.taskId !== null ? { projectId: state.projectId, taskId: state.taskId } : null;

// Writes run one after another with concatMap: none is dropped, and each one's outcome reaches its form.

export const createEntry = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TimeTrackingApiService)) =>
    actions$.pipe(
      ofType(TimeEntriesPageActions.createSubmitted),
      withLatestFrom(store.select(timeTrackingFeature.selectTimeTrackingState)),
      concatMap(([{ entry, submissionId }, state]) => {
        const task = openTask(state);
        return task === null
          ? of(TimeEntriesApiActions.entrySaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.create(task.projectId, task.taskId, entry).pipe(
              map((created) => TimeEntriesApiActions.entrySaved({ entry: created, message: 'time.logged', submissionId })),
              catchError((error: unknown) => of(TimeEntriesApiActions.entrySaveFailed({ error: toApiError(error), submissionId }))),
            );
      }),
    ),
  { functional: true },
);

export const updateEntry = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TimeTrackingApiService)) =>
    actions$.pipe(
      ofType(TimeEntriesPageActions.updateSubmitted),
      withLatestFrom(store.select(timeTrackingFeature.selectTimeTrackingState)),
      concatMap(([{ entryId, changes, submissionId }, state]) => {
        const task = openTask(state);
        return task === null
          ? of(TimeEntriesApiActions.entrySaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.update(task.projectId, task.taskId, entryId, changes).pipe(
              map((saved) => TimeEntriesApiActions.entrySaved({ entry: saved, message: 'time.saved', submissionId })),
              catchError((error: unknown) => of(TimeEntriesApiActions.entrySaveFailed({ error: toApiError(error), submissionId }))),
            );
      }),
    ),
  { functional: true },
);

export const deleteEntry = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TimeTrackingApiService)) =>
    actions$.pipe(
      ofType(TimeEntriesPageActions.deleteConfirmed),
      withLatestFrom(store.select(timeTrackingFeature.selectTimeTrackingState)),
      concatMap(([{ entryId }, state]) => {
        const task = openTask(state);
        return task === null
          ? of(TimeEntriesApiActions.entrySaveFailed({ error: { code: 'unexpected', fields: {} } }))
          : api.delete(task.projectId, task.taskId, entryId).pipe(
              map(() => TimeEntriesApiActions.entryDeleted({ entryId })),
              catchError((error: unknown) => of(TimeEntriesApiActions.entrySaveFailed({ error: toApiError(error) }))),
            );
      }),
    ),
  { functional: true },
);

export const loadSummary = createEffect(
  (actions$ = inject(Actions), api = inject(TimeTrackingApiService)) =>
    actions$.pipe(
      ofType(TimeReportPageActions.opened),
      // A newer filter makes the answer to the older one stale.
      switchMap(({ projectId, filters }) =>
        api.summary(projectId, filters).pipe(
          untilTenantChanges(actions$),
          map((summary) => TimeReportApiActions.summaryLoaded({ projectId, summary })),
          catchError((error: unknown) => of(TimeReportApiActions.summaryLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const exportCsv = createEffect(
  (actions$ = inject(Actions), api = inject(TimeTrackingApiService)) =>
    actions$.pipe(
      ofType(TimeReportPageActions.exportClicked),
      concatMap(({ projectId, filters }) =>
        api.summaryCsv(projectId, filters).pipe(
          map((blob) => {
            downloadBlob(blob, 'time-summary.csv');
            return TimeReportApiActions.exportDone();
          }),
          catchError((error: unknown) =>
            from(readBlobError(error)).pipe(map((read) => TimeReportApiActions.exportFailed({ error: toApiError(read) }))),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(
        TimeEntriesApiActions.entrySaved,
        TimeEntriesApiActions.entryDeleted,
        TimeEntriesApiActions.entrySaveFailed,
        TimeEntriesApiActions.taskTimeLoadFailed,
        TimeReportApiActions.exportFailed,
      ),
      tap((action) => {
        switch (action.type) {
          case TimeEntriesApiActions.entrySaved.type:
            notifier.success(action.message);
            break;
          case TimeEntriesApiActions.entryDeleted.type:
            notifier.success('time.deleted');
            break;
          default:
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
