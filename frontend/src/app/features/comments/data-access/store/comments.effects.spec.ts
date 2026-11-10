import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { Notifier } from '../../../../core/notifications/notifier';
import { CommentsApiService } from '../comments-api.service';
import { CommentsApiActions, CommentsPageActions } from './comments.actions';
import { createComment, deleteComment, loadComments, notify, refreshComments, updateComment } from './comments.effects';
import { commentsFeature, initialCommentsState } from './comments.feature';
import { aComment } from './testing';

function withComment(state: typeof initialCommentsState) {
  return { ...state, ids: ['a'], entities: { a: aComment({ id: 'a', rowVersion: 'rv1' }) } };
}

function storeWith(state: typeof initialCommentsState): Store {
  return { select: (selector: unknown) => of(selector === commentsFeature.selectCommentsState ? state : undefined) } as unknown as Store;
}

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const open = { ...initialCommentsState, projectId: 'p1', taskId: 't1' };
const input = { body: 'Hi @Ana', mentionedUserIds: ['u2'] };

describe('comments effects', () => {
  it('loads the thread of the opened task', () => {
    const comments = [aComment({ id: 'a' })];
    const api = { list: vi.fn(() => of(comments)) } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.opened({ projectId: 'p1', taskId: 't1' })) as Actions;

    expect(run(loadComments(actions$, api))).toEqual([CommentsApiActions.commentsLoaded({ taskId: 't1', comments })]);
    expect(api.list).toHaveBeenCalledWith('p1', 't1');
  });

  it('creates a comment and hands the outcome to its form', () => {
    const created = aComment({ id: 'a' });
    const api = { create: vi.fn(() => of(created)) } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.createSubmitted({ comment: input, submissionId: 3 })) as Actions;

    const emitted = run(createComment(actions$, storeWith(open), api));

    expect(api.create).toHaveBeenCalledWith('p1', 't1', input);
    expect(emitted).toEqual([CommentsApiActions.commentSaved({ comment: created, message: 'comments.created', submissionId: 3 })]);
  });

  it('reports a rejected comment with the API error and the submission id', () => {
    const api = {
      create: vi.fn(() =>
        throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'TaskComments.BodyTooLong' } })),
      ),
    } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.createSubmitted({ comment: input, submissionId: 5 })) as Actions;

    const emitted = run(createComment(actions$, storeWith(open), api));

    expect(emitted).toEqual([
      CommentsApiActions.commentSaveFailed({ error: { code: 'TaskComments.BodyTooLong', fields: {} }, submissionId: 5 }),
    ]);
  });

  it('does not call the API without an open thread', () => {
    const api = { create: vi.fn() } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.createSubmitted({ comment: input, submissionId: 1 })) as Actions;

    const emitted = run(createComment(actions$, storeWith(initialCommentsState), api));

    expect(api.create).not.toHaveBeenCalled();
    expect(emitted).toEqual([
      CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 1 }),
    ]);
  });

  it('updates a comment', () => {
    const saved = aComment({ id: 'a', isEdited: true });
    const api = { update: vi.fn(() => of(saved)) } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.updateSubmitted({ commentId: 'a', changes: input, rowVersion: 'rv1', submissionId: 2 })) as Actions;

    const emitted = run(updateComment(actions$, storeWith(withComment(open)), api));

    expect(api.update).toHaveBeenCalledWith('p1', 't1', 'a', input, 'rv1');
    expect(emitted).toEqual([CommentsApiActions.commentSaved({ comment: saved, message: 'comments.saved', submissionId: 2 })]);
  });

  it('sends the version the editor opened with, not the one a background reload put in the store', () => {
    const api = { update: vi.fn(() => of(aComment({ id: 'a' }))) } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.updateSubmitted({ commentId: 'a', changes: input, rowVersion: 'rv-opened', submissionId: 11 })) as Actions;

    run(updateComment(actions$, storeWith(withComment(open)), api));

    expect(api.update).toHaveBeenCalledWith('p1', 't1', 'a', input, 'rv-opened');
  });

  it('does not update a comment the store does not hold', () => {
    const api = { update: vi.fn() } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.updateSubmitted({ commentId: 'zz', changes: input, rowVersion: 'rv1', submissionId: 4 })) as Actions;

    const emitted = run(updateComment(actions$, storeWith(withComment(open)), api));

    expect(api.update).not.toHaveBeenCalled();
    expect(emitted).toEqual([CommentsApiActions.commentSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 4 })]);
  });

  it('refreshes the thread for the editor, tagged with its submission id', () => {
    const comments = [aComment({ id: 'a', rowVersion: 'rv2' })];
    const api = { list: vi.fn(() => of(comments)) } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.refreshRequested({ submissionId: 9 })) as Actions;

    const emitted = run(refreshComments(actions$, storeWith(open), api));

    expect(api.list).toHaveBeenCalledWith('p1', 't1');
    expect(emitted).toEqual([CommentsApiActions.commentsRefreshed({ taskId: 't1', comments, submissionId: 9 })]);
  });

  it('keeps a stale edit out of the snackbar but shows other errors', () => {
    const notifier = { success: vi.fn(), error: vi.fn() } as unknown as Notifier;
    const stale = { code: 'Concurrency.Stale', fields: {} };
    const other = { code: 'unexpected', fields: {} };
    const actions$ = of(
      CommentsApiActions.commentSaveFailed({ error: stale, submissionId: 1 }),
      CommentsApiActions.commentSaveFailed({ error: other, submissionId: 2 }),
    ) as Actions;

    run(notify(actions$, notifier));

    expect(notifier.error).toHaveBeenCalledTimes(1);
    expect(notifier.error).toHaveBeenCalledWith(other);
  });

  it('deletes a comment', () => {
    const api = { delete: vi.fn(() => of(undefined)) } as unknown as CommentsApiService;
    const actions$ = of(CommentsPageActions.deleteConfirmed({ commentId: 'a' })) as Actions;

    const emitted = run(deleteComment(actions$, storeWith(open), api));

    expect(api.delete).toHaveBeenCalledWith('p1', 't1', 'a');
    expect(emitted).toEqual([CommentsApiActions.commentDeleted({ commentId: 'a' })]);
  });

  it('shows the message of a saved comment, the deletion and the errors', () => {
    const notifier = { success: vi.fn(), error: vi.fn() } as unknown as Notifier;
    const error = { code: 'unexpected', fields: {} };
    const actions$ = of(
      CommentsApiActions.commentSaved({ comment: aComment({ id: 'a' }), message: 'comments.created', submissionId: 1 }),
      CommentsApiActions.commentDeleted({ commentId: 'a' }),
      CommentsApiActions.commentSaveFailed({ error }),
    ) as Actions;

    run(notify(actions$, notifier));

    expect(notifier.success).toHaveBeenNthCalledWith(1, 'comments.created');
    expect(notifier.success).toHaveBeenNthCalledWith(2, 'comments.deleted');
    expect(notifier.error).toHaveBeenCalledWith(error);
  });
});
