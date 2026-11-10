import { CommentsApiActions, CommentsPageActions } from './comments.actions';
import { commentsFeature, initialCommentsState } from './comments.feature';
import { aComment } from './testing';

const reduce = commentsFeature.reducer;

describe('comments reducer: row versions', () => {
  const opened = reduce(initialCommentsState, CommentsPageActions.opened({ projectId: 'p1', taskId: 't1' }));
  const loaded = reduce(
    opened,
    CommentsApiActions.commentsLoaded({ taskId: 't1', comments: [aComment({ id: 'a', rowVersion: 'rv1' })] }),
  );

  it('keeps the new row version a save returns', () => {
    const state = reduce(
      loaded,
      CommentsApiActions.commentSaved({ comment: aComment({ id: 'a', rowVersion: 'rv2' }), message: 'comments.saved' }),
    );

    expect(state.entities['a']?.rowVersion).toBe('rv2');
  });

  it('takes the refreshed thread of the open task only', () => {
    const fresh = aComment({ id: 'a', body: 'Theirs', rowVersion: 'rv5' });

    const state = reduce(loaded, CommentsApiActions.commentsRefreshed({ taskId: 't1', comments: [fresh] }));
    const other = reduce(loaded, CommentsApiActions.commentsRefreshed({ taskId: 't2', comments: [fresh] }));

    expect(state.entities['a']).toEqual(fresh);
    expect(other.entities['a']?.rowVersion).toBe('rv1');
  });
});
