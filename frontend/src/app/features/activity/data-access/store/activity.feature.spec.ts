import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { ActivityApiActions, ActivityPageActions } from './activity.actions';
import { ActivityState, activityFeature, initialActivityState } from './activity.feature';
import { anEntry } from './testing';

const { reducer } = activityFeature;
const page = (ids: string[], nextCursor: string | null) => ({
  items: ids.map((id) => anEntry({ id })),
  nextCursor,
});
const loaded = (state: ActivityState, result: ReturnType<typeof page>, taskId: string | null = null, append = false) =>
  reducer(state, ActivityApiActions.pageLoaded({ scope: 'project', projectId: 'p1', taskId, append, result }));
const opened = reducer(initialActivityState, ActivityPageActions.opened({ scope: 'project', projectId: 'p1', taskId: null }));

describe('activity reducer', () => {
  it('marks the feed as loading when opened', () => {
    expect(opened.project).toMatchObject({ projectId: 'p1', loading: true, items: [] });
  });

  it('keeps the project feed and a task history apart', () => {
    const state = reducer(opened, ActivityPageActions.opened({ scope: 'task', projectId: 'p1', taskId: 't1' }));

    expect(state.task).toMatchObject({ taskId: 't1', loading: true });
    expect(state.project.taskId).toBeNull();
  });

  it('appends the next page and drops entries it already has', () => {
    const first = loaded(opened, page(['a', 'b'], 'c1'));
    const second = loaded(reducer(first, ActivityPageActions.loadMoreClicked({ scope: 'project' })), page(['b', 'c'], null), null, true);

    expect(second.project.items.map((item) => item.id)).toEqual(['a', 'b', 'c']);
    expect(second.project).toMatchObject({ nextCursor: null, loading: false });
    expect(activityFeature.selectProjectHasMore.projector(second.project)).toBe(false);
    expect(activityFeature.selectProjectHasMore.projector(first.project)).toBe(true);
  });

  it('replaces the items when the first page is reloaded', () => {
    const first = loaded(opened, page(['a', 'b'], 'c1'));
    const again = loaded(
      reducer(first, ActivityPageActions.opened({ scope: 'project', projectId: 'p1', taskId: null })),
      page(['z'], null),
    );

    expect(again.project.items.map((item) => item.id)).toEqual(['z']);
  });

  it('ignores a response for a feed that is no longer the open one', () => {
    expect(loaded(opened, page(['a'], null), 't9')).toBe(opened);
  });

  it('keeps the entries when a later page fails', () => {
    const first = loaded(opened, page(['a', 'b'], 'c1'));
    const failed = reducer(
      first,
      ActivityApiActions.loadFailed({ scope: 'project', projectId: 'p1', taskId: null, error: { code: 'unexpected', fields: {} } }),
    );

    expect(failed.project.items).toHaveLength(2);
    expect(failed.project).toMatchObject({ loading: false, error: { code: 'unexpected' } });
  });

  it.each([ShellActions.logoutClicked(), AuthApiActions.sessionExpired()])('resets on $type', (action) => {
    expect(reducer(loaded(opened, page(['a'], null)), action)).toEqual(initialActivityState);
  });
});
