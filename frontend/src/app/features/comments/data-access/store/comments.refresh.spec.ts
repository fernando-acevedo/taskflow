import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of } from 'rxjs';
import { RealtimeActions } from '@core/realtime/realtime.actions';
import { CommentsApiService } from '../comments-api.service';
import { reloadOpenThread } from './comments.effects';
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

describe('reloadOpenThread', () => {
  const store = (state: typeof initialCommentsState) => ({ select: () => of(state) }) as unknown as Store;
  const open = { ...initialCommentsState, projectId: 'p1', taskId: 't1' };
  const run = (effect: Observable<Action>): Action[] => {
    const emitted: Action[] = [];
    effect.subscribe((action) => emitted.push(action));
    return emitted;
  };

  it('reloads the thread when somebody else changed a comment of the open task', () => {
    const comments = [aComment({ id: 'a' })];
    const api = { list: vi.fn(() => of(comments)) } as unknown as CommentsApiService;
    const actions$ = of(RealtimeActions.commentsChanged({ projectId: 'p1', taskId: 't1' })) as Actions;

    expect(run(reloadOpenThread(actions$, store(open), api))).toEqual([CommentsApiActions.commentsRefreshed({ taskId: 't1', comments })]);
  });

  it('leaves alone a thread that is not open, and reloads the open one on a resync', () => {
    const api = { list: vi.fn(() => of([])) } as unknown as CommentsApiService;

    const other = of(RealtimeActions.commentsChanged({ projectId: 'p1', taskId: 't2' })) as Actions;
    expect(run(reloadOpenThread(other, store(open), api))).toEqual([]);
    expect(api.list).not.toHaveBeenCalled();

    const resync = of(RealtimeActions.resyncRequested({ projectId: 'p1' })) as Actions;
    expect(run(reloadOpenThread(resync, store(open), api))).toHaveLength(1);
    expect(run(reloadOpenThread(resync, store(initialCommentsState), api))).toEqual([]);
  });
});
