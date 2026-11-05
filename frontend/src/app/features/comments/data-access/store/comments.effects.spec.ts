import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { Notifier } from '../../../../core/notifications/notifier';
import { CommentsApiService } from '../comments-api.service';
import { CommentsApiActions, CommentsPageActions } from './comments.actions';
import { createComment, deleteComment, loadComments, notify, updateComment } from './comments.effects';
import { commentsFeature, initialCommentsState } from './comments.feature';
import { aComment } from './testing';

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
    const actions$ = of(CommentsPageActions.updateSubmitted({ commentId: 'a', changes: input, submissionId: 2 })) as Actions;

    const emitted = run(updateComment(actions$, storeWith(open), api));

    expect(api.update).toHaveBeenCalledWith('p1', 't1', 'a', input);
    expect(emitted).toEqual([CommentsApiActions.commentSaved({ comment: saved, message: 'comments.saved', submissionId: 2 })]);
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
