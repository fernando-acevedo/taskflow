import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { EMPTY, catchError, concatMap, filter, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { ApiError, isStale, toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { LabelsApiActions } from '../../../labels/data-access/store/labels.actions';
import { TasksApiService } from '../tasks-api.service';
import { TasksApiActions, TasksPageActions } from './tasks.actions';
import { tasksFeature } from './tasks.feature';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';

export const loadTasks = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      // Editing or deleting a label changes the labels each task carries. A move needs no reload: its answer
      // carries the neighbors' new positions and versions. A stale move does (see `reloadAfterStaleMove`).
      ofType(
        TasksPageActions.opened,
        LabelsApiActions.labelSaved,
        LabelsApiActions.labelDeleted,
      ),
      withLatestFrom(store.select(tasksFeature.selectProjectId)),
      map(([action, current]) => (action.type === TasksPageActions.opened.type ? action.projectId : current)),
      filter((projectId): projectId is string => projectId !== null),
      switchMap((projectId) =>
        api.list(projectId).pipe(
          untilTenantChanges(actions$),
          map((tasks) => TasksApiActions.tasksLoaded({ projectId, tasks })),
          catchError((error: unknown) => of(TasksApiActions.tasksLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/**
 * The answer when the task an action refers to is no longer in the store (a reload removed it): a failure the
 * user sees, instead of a request without the task's version. Likewise, a create without an open project
 * fails as `unexpected` instead of calling `/api/projects//tasks`.
 */
const taskGone: ApiError = { code: 'Tasks.NotFound', fields: {} };

// Writes run one after another with concatMap: none is dropped, and each one's outcome reaches its form.

export const createTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.createSubmitted),
      withLatestFrom(store.select(tasksFeature.selectProjectId)),
      concatMap(([{ task, submissionId }, projectId]) =>
        projectId === null
          ? of(TasksApiActions.taskSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.create(projectId, task).pipe(
              map((created) => TasksApiActions.taskSaved({ task: created, message: 'tasks.created', submissionId })),
              catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error), submissionId }))),
            ),
      ),
    ),
  { functional: true },
);

export const updateTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.updateSubmitted),
      withLatestFrom(store.select(tasksFeature.selectEntities)),
      concatMap(([{ taskId, changes, rowVersion, submissionId }, tasks]) => {
        const task = tasks[taskId];
        return task === undefined
          ? of(TasksApiActions.taskSaveFailed({ error: taskGone, submissionId }))
          : api.update({ ...task, rowVersion }, changes).pipe(
              map((saved) => TasksApiActions.taskSaved({ task: saved, message: 'tasks.saved', submissionId })),
              catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error), submissionId }))),
            );
      }),
    ),
  { functional: true },
);

export const deleteTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.deleteConfirmed),
      withLatestFrom(store.select(tasksFeature.selectEntities)),
      concatMap(([{ taskId }, tasks]) => {
        const task = tasks[taskId];
        return task === undefined
          ? of(TasksApiActions.taskSaveFailed({ error: taskGone }))
          : api.delete(task).pipe(
              map(() => TasksApiActions.taskDeleted({ taskId })),
              catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error) }))),
            );
      }),
    ),
  { functional: true },
);

/**
 * The reducer has already moved the card; this tells the API. The task is read from `moveUndo`, which holds
 * its version from before the drop, the one the API knows.
 */
export const moveTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.taskDropped),
      withLatestFrom(store.select(tasksFeature.selectMoveUndo)),
      concatMap(([{ taskId, status, position }, undo]) => {
        const before = undo?.find((task) => task.id === taskId);
        if (!before) {
          return EMPTY;
        }
        return api.move(before, status, position).pipe(
          map((result) => TasksApiActions.taskMoved({ result })),
          catchError((error: unknown) => of(TasksApiActions.taskMoveFailed({ error: toApiError(error) }))),
        );
      }),
    ),
  { functional: true },
);

/** Another user changed the task: the reducer rolls the optimistic move back, and the board reloads. */
export const reloadAfterStaleMove = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksApiActions.taskMoveFailed),
      filter(({ error }) => isStale(error)),
      withLatestFrom(store.select(tasksFeature.selectProjectId)),
      map(([, projectId]) => projectId),
      filter((projectId): projectId is string => projectId !== null),
      switchMap((projectId) =>
        api.list(projectId).pipe(
          untilTenantChanges(actions$),
          map((tasks) => TasksApiActions.tasksLoaded({ projectId, tasks })),
          catchError((error: unknown) => of(TasksApiActions.tasksLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** The edit dialog's "Reload": the server's version of the task, so the next save carries its row version. */
export const refreshTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.refreshRequested),
      withLatestFrom(store.select(tasksFeature.selectProjectId)),
      concatMap(([{ taskId, submissionId }, projectId]) =>
        projectId === null
          ? of(TasksApiActions.taskSaveFailed({ error: taskGone, submissionId }))
          : api.get(projectId, taskId).pipe(
              map((task) => TasksApiActions.taskRefreshed({ task, submissionId })),
              catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error), submissionId }))),
            ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(
        TasksApiActions.taskSaved,
        TasksApiActions.taskDeleted,
        TasksApiActions.taskSaveFailed,
        TasksApiActions.taskMoveFailed,
      ),
      tap((action) => {
        switch (action.type) {
          case TasksApiActions.taskSaved.type:
            notifier.success(action.message);
            break;
          case TasksApiActions.taskDeleted.type:
            notifier.success('tasks.deleted');
            break;
          case TasksApiActions.taskMoveFailed.type:
            if (isStale(action.error)) {
              notifier.warning('tasks.moveStale');
            } else {
              notifier.error(action.error);
            }
            break;
          default:
            // A stale save is shown inside the dialog that submitted it, not as a snackbar.
            if (!(isStale(action.error) && action.submissionId !== undefined)) {
              notifier.error(action.error);
            }
        }
      }),
    ),
  { functional: true, dispatch: false },
);
