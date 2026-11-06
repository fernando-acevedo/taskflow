import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { CommentsApiService } from '../comments-api.service';
import { CommentsApiActions, CommentsPageActions } from './comments.actions';
import { commentsFeature } from './comments.feature';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';

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
      concatMap(([{ commentId, changes, submissionId }, state]) => {
        const thread = openThread(state);
        return thread === null
          ? of(CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId }))
          : api.update(thread.projectId, thread.taskId, commentId, changes).pipe(
              map((saved) => CommentsApiActions.commentSaved({ comment: saved, message: 'comments.saved', submissionId })),
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
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
