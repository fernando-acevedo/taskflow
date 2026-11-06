import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { TenantActions } from '../../../../core/tenant/store/tenant.actions';
import { ApiError } from '../../../../core/http/api-error';
import { ActivityEntry } from '../activity-api.service';
import { ActivityApiActions, ActivityPageActions, ActivityScope } from './activity.actions';

export interface ActivityFeed {
  projectId: string | null;
  taskId: string | null;
  items: readonly ActivityEntry[];
  /** The last page loaded (0 before the first). */
  page: number;
  totalCount: number;
  loading: boolean;
  error: ApiError | null;
}

export interface ActivityState {
  project: ActivityFeed;
  task: ActivityFeed;
}

const emptyFeed: ActivityFeed = { projectId: null, taskId: null, items: [], page: 0, totalCount: 0, loading: false, error: null };

export const initialActivityState: ActivityState = { project: emptyFeed, task: emptyFeed };

const matches = (feed: ActivityFeed, projectId: string, taskId: string | null): boolean =>
  feed.projectId === projectId && feed.taskId === taskId;

function withFeed(state: ActivityState, scope: ActivityScope, feed: ActivityFeed): ActivityState {
  return { ...state, [scope]: feed };
}

export const activityFeature = createFeature({
  name: 'activity',
  reducer: createReducer(
    initialActivityState,
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, TenantActions.tenantSwitched, (): ActivityState => initialActivityState),
    on(ActivityPageActions.opened, (state, { scope, projectId, taskId }): ActivityState => {
      const feed = state[scope];
      // Reopening the same feed keeps what is shown while it refreshes.
      const base = matches(feed, projectId, taskId) ? feed : { ...emptyFeed, projectId, taskId };
      return withFeed(state, scope, { ...base, loading: true, error: null });
    }),
    on(ActivityPageActions.loadMoreClicked, (state, { scope }): ActivityState =>
      withFeed(state, scope, { ...state[scope], loading: true, error: null }),
    ),
    on(ActivityApiActions.pageLoaded, (state, { scope, projectId, taskId, result }): ActivityState => {
      const feed = state[scope];
      if (!matches(feed, projectId, taskId)) {
        return state;
      }
      const known = result.page > 1 ? feed.items : [];
      const seen = new Set(known.map((item) => item.id));
      const items = [...known, ...result.items.filter((item) => !seen.has(item.id))];
      return withFeed(state, scope, { ...feed, items, page: result.page, totalCount: result.totalCount, loading: false });
    }),
    on(ActivityApiActions.loadFailed, (state, { scope, projectId, taskId, error }): ActivityState =>
      matches(state[scope], projectId, taskId) ? withFeed(state, scope, { ...state[scope], loading: false, error }) : state,
    ),
  ),
  extraSelectors: ({ selectProject, selectTask }) => ({
    selectProjectHasMore: createSelector(selectProject, (feed) => feed.items.length < feed.totalCount),
    selectTaskHasMore: createSelector(selectTask, (feed) => feed.items.length < feed.totalCount),
  }),
});
