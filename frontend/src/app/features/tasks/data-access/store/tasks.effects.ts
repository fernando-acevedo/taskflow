import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { EMPTY, catchError, concatMap, exhaustMap, filter, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { Task, TasksApiService } from '../tasks-api.service';
import { TasksApiActions, TasksPageActions } from './tasks.actions';
import { tasksFeature } from './tasks.feature';

export const loadTasks = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.opened, TasksApiActions.taskMoved, TasksApiActions.taskMoveFailed),
      withLatestFrom(store.select(tasksFeature.selectProjectId)),
      map(([action, current]) => (action.type === TasksPageActions.opened.type ? action.projectId : current)),
      filter((projectId): projectId is string => projectId !== null),
      switchMap((projectId) =>
        api.list(projectId).pipe(
          map((tasks) => TasksApiActions.tasksLoaded({ projectId, tasks })),
          catchError((error: unknown) => of(TasksApiActions.tasksLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const createTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.createSubmitted),
      withLatestFrom(store.select(tasksFeature.selectProjectId)),
      exhaustMap(([{ task }, projectId]) =>
        api.create(projectId ?? '', task).pipe(
          map((created) => TasksApiActions.taskSaved({ task: created, message: 'tasks.created' })),
          catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error) }))),
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
      exhaustMap(([{ taskId, changes }, tasks]) =>
        api.update(tasks[taskId] as Task, changes).pipe(
          map((task) => TasksApiActions.taskSaved({ task, message: 'tasks.saved' })),
          catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const deleteTask = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(TasksApiService)) =>
    actions$.pipe(
      ofType(TasksPageActions.deleteConfirmed),
      withLatestFrom(store.select(tasksFeature.selectEntities)),
      exhaustMap(([{ taskId }, tasks]) =>
        api.delete(tasks[taskId] as Task).pipe(
          map(() => TasksApiActions.taskDeleted({ taskId })),
          catchError((error: unknown) => of(TasksApiActions.taskSaveFailed({ error: toApiError(error) }))),
        ),
      ),
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
          map((task) => TasksApiActions.taskMoved({ task })),
          catchError((error: unknown) => of(TasksApiActions.taskMoveFailed({ error: toApiError(error) }))),
        );
      }),
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
          default:
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
