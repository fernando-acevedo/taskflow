import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { ActivityApiActions, ActivityPageActions } from './activity.actions';
import { ActivityState, activityFeature, initialActivityState } from './activity.feature';
import { anEntry } from './testing';

const { reducer } = activityFeature;
const page = (number: number, ids: string[], totalCount: number) => ({
  items: ids.map((id) => anEntry({ id })),
  page: number,
  pageSize: 2,
  totalCount,
});
const loaded = (state: ActivityState, result: ReturnType<typeof page>, taskId: string | null = null) =>
  reducer(state, ActivityApiActions.pageLoaded({ scope: 'project', projectId: 'p1', taskId, result }));
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
    const first = loaded(opened, page(1, ['a', 'b'], 3));
    const second = loaded(reducer(first, ActivityPageActions.loadMoreClicked({ scope: 'project' })), page(2, ['b', 'c'], 3));

    expect(second.project.items.map((item) => item.id)).toEqual(['a', 'b', 'c']);
    expect(second.project).toMatchObject({ page: 2, loading: false });
    expect(activityFeature.selectProjectHasMore.projector(second.project)).toBe(false);
    expect(activityFeature.selectProjectHasMore.projector(first.project)).toBe(true);
  });

  it('replaces the items when the first page is reloaded', () => {
    const first = loaded(opened, page(1, ['a', 'b'], 2));
    const again = loaded(
      reducer(first, ActivityPageActions.opened({ scope: 'project', projectId: 'p1', taskId: null })),
      page(1, ['z'], 1),
    );

    expect(again.project.items.map((item) => item.id)).toEqual(['z']);
  });

  it('ignores a response for a feed that is no longer the open one', () => {
    expect(loaded(opened, page(1, ['a'], 1), 't9')).toBe(opened);
  });

  it('keeps the entries when a later page fails', () => {
    const first = loaded(opened, page(1, ['a', 'b'], 4));
    const failed = reducer(
      first,
      ActivityApiActions.loadFailed({ scope: 'project', projectId: 'p1', taskId: null, error: { code: 'unexpected', fields: {} } }),
    );

    expect(failed.project.items).toHaveLength(2);
    expect(failed.project).toMatchObject({ loading: false, error: { code: 'unexpected' } });
  });

  it.each([ShellActions.logoutClicked(), AuthApiActions.sessionExpired()])('resets on $type', (action) => {
    expect(reducer(loaded(opened, page(1, ['a'], 1)), action)).toEqual(initialActivityState);
  });
});
