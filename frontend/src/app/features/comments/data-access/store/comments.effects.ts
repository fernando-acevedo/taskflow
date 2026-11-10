import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { EMPTY, catchError, concatMap, filter, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { isStale, toApiError } from '@shared/data-access/api-error';
import { RealtimeActions } from '@core/realtime/realtime.actions';
import { Notifier } from '@core/notifications/notifier';
import { CommentsApiService } from '../comments-api.service';
import { CommentsApiActions, CommentsPageActions } from './comments.actions';
import { commentsFeature } from './comments.feature';
import { untilTenantChanges } from '@core/tenant/until-tenant-changes';

export const loadComments = createEffect(
  (actions$ = inject(Actions), api = inject(CommentsApiService)) =>
    actions$.pipe(
      ofType(CommentsPageActions.opened),
      switchMap(({ projectId, taskId }) =>
        api.list(projectId, taskId).pipe(
          untilTenantChanges(actions$),
          map((comments) => CommentsApiActions.commentsLoaded({ taskId, comments })),
          catchError((error: unknown) => of(CommentsApiActions.commentsLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/**
 * Somebody else commented, edited or deleted a comment (ADR 0023), or events may have been missed: reload the
 * thread if it is the one open. The merged list replaces the old one, so an inline edit in progress keeps its own draft.
 */
export const reloadOpenThread = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(CommentsApiService)) =>
    actions$.pipe(
      ofType(RealtimeActions.commentsChanged, RealtimeActions.resyncRequested),
      withLatestFrom(store.select(commentsFeature.selectCommentsState)),
      filter(
        ([action, state]) =>
          state.taskId !== null &&
          state.projectId === action.projectId &&
          (action.type === RealtimeActions.resyncRequested.type || action.taskId === state.taskId),
      ),
      switchMap(([, state]) =>
        api.list(state.projectId!, state.taskId!).pipe(
          untilTenantChanges(actions$),
          map((comments) => CommentsApiActions.commentsRefreshed({ taskId: state.taskId!, comments })),
          // Best effort: the thread keeps what it had.
          catchError(() => EMPTY),
        ),
      ),
    ),
  { functional: true },
);

/** The open thread's project and task, or null when none is open. */
const openThread = (state: { projectId: string | null; taskId: string | null }) =>
  state.projectId !== null && state.taskId !== null ? { projectId: state.projectId, taskId: state.taskId } : null;

// Writes run one after another with concatMap: none is dropped, and each one's outcome reaches its form.

export const createComment = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(CommentsApiService)) =>
    actions$.pipe(
      ofType(CommentsPageActions.createSubmitted),
      withLatestFrom(store.select(commentsFeature.selectCommentsState)),
      concatMap(([{ comment, submissionId }, state]) => {
        const thread = openThread(state);
        return thread === null
          ? of(CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.create(thread.projectId, thread.taskId, comment).pipe(
              map((created) => CommentsApiActions.commentSaved({ comment: created, message: 'comments.created', submissionId })),
              catchError((error: unknown) => of(CommentsApiActions.commentSaveFailed({ error: toApiError(error), submissionId }))),
            );
      }),
    ),
  { functional: true },
);

export const updateComment = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(CommentsApiService)) =>
    actions$.pipe(
      ofType(CommentsPageActions.updateSubmitted),
      withLatestFrom(store.select(commentsFeature.selectCommentsState)),
      concatMap(([{ commentId, changes, rowVersion, submissionId }, state]) => {
        const thread = openThread(state);
        const current = state.entities[commentId];
        return thread === null || current === undefined
          ? of(CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.update(thread.projectId, thread.taskId, commentId, changes, rowVersion).pipe(
              map((saved) => CommentsApiActions.commentSaved({ comment: saved, message: 'comments.saved', submissionId })),
              catchError((error: unknown) => of(CommentsApiActions.commentSaveFailed({ error: toApiError(error), submissionId }))),
            );
      }),
    ),
  { functional: true },
);

/** The inline editor's "Reload": the server's thread, so the next save carries the current row version. */
export const refreshComments = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(CommentsApiService)) =>
    actions$.pipe(
      ofType(CommentsPageActions.refreshRequested),
      withLatestFrom(store.select(commentsFeature.selectCommentsState)),
      concatMap(([{ submissionId }, state]) => {
        const thread = openThread(state);
        return thread === null
          ? of(CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.list(thread.projectId, thread.taskId).pipe(
              map((comments) => CommentsApiActions.commentsRefreshed({ taskId: thread.taskId, comments, submissionId })),
              catchError((error: unknown) => of(CommentsApiActions.commentSaveFailed({ error: toApiError(error), submissionId }))),
            );
      }),
    ),
  { functional: true },
);

export const deleteComment = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(CommentsApiService)) =>
    actions$.pipe(
      ofType(CommentsPageActions.deleteConfirmed),
      withLatestFrom(store.select(commentsFeature.selectCommentsState)),
      concatMap(([{ commentId }, state]) => {
        const thread = openThread(state);
        return thread === null
          ? of(CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} } }))
          : api.delete(thread.projectId, thread.taskId, commentId).pipe(
              map(() => CommentsApiActions.commentDeleted({ commentId })),
              catchError((error: unknown) => of(CommentsApiActions.commentSaveFailed({ error: toApiError(error) }))),
            );
      }),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(
        CommentsApiActions.commentSaved,
        CommentsApiActions.commentDeleted, CommentsApiActions.commentSaveFailed, CommentsApiActions.commentsLoadFailed),
      tap((action) => {
        switch (action.type) {
          case CommentsApiActions.commentSaved.type:
            notifier.success(action.message);
            break;
          case CommentsApiActions.commentDeleted.type:
            notifier.success('comments.deleted');
            break;
          default:
            // A stale edit is shown inside the editor that submitted it, not as a snackbar.
            if (!(isStale(action.error) && 'submissionId' in action && action.submissionId !== undefined)) {
              notifier.error(action.error);
            }
        }
      }),
    ),
  { functional: true, dispatch: false },
);
