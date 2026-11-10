import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { aComment } from './testing';
import { CommentsApiActions, CommentsPageActions } from './comments.actions';
import { commentsFeature, initialCommentsState } from './comments.feature';


const reduce = commentsFeature.reducer;

describe('comments reducer', () => {
  const opened = reduce(initialCommentsState, CommentsPageActions.opened({ projectId: 'p1', taskId: 't1' }));

  it('keeps the thread oldest first', () => {
    const late = aComment({ id: 'b', createdAt: '2026-11-02T10:00:00Z' });
    const early = aComment({ id: 'a' });

    const state = reduce(opened, CommentsApiActions.commentsLoaded({ taskId: 't1', comments: [late, early] }));

    expect(state.ids).toEqual(['a', 'b']);
    expect(state.loading).toBe(false);
  });

  it("drops another task's thread when a different task opens", () => {
    const loaded = reduce(opened, CommentsApiActions.commentsLoaded({ taskId: 't1', comments: [aComment({ id: 'a' })] }));

    const state = reduce(loaded, CommentsPageActions.opened({ projectId: 'p1', taskId: 't2' }));

    expect(state.ids).toEqual([]);
    expect(state.taskId).toBe('t2');
  });

  it("ignores a load that answers for a task that's no longer open", () => {
    const state = reduce(opened, CommentsApiActions.commentsLoaded({ taskId: 'other', comments: [aComment({ id: 'a' })] }));

    expect(state.ids).toEqual([]);
  });

  it('adds, replaces and removes comments', () => {
    let state = reduce(opened, CommentsApiActions.commentSaved({ comment: aComment({ id: 'a' }), message: 'comments.created' }));
    state = reduce(
      state,
      CommentsApiActions.commentSaved({ comment: aComment({ id: 'a', body: 'changed', isEdited: true }), message: 'comments.saved' }),
    );
    expect(state.entities['a']?.body).toBe('changed');
    expect(state.ids).toHaveLength(1);

    state = reduce(state, CommentsApiActions.commentDeleted({ commentId: 'a' }));
    expect(state.ids).toEqual([]);
  });

  it.each([ShellActions.logoutClicked(), AuthApiActions.sessionExpired()])('resets on $type', (action) => {
    const loaded = reduce(opened, CommentsApiActions.commentsLoaded({ taskId: 't1', comments: [aComment({ id: 'a' })] }));

    expect(reduce(loaded, action)).toEqual(initialCommentsState);
  });
});
