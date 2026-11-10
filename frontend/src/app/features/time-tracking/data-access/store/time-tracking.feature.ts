import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { getRouterSelectors } from '@ngrx/router-store';
import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { ApiError } from '@shared/data-access/api-error';
import { TimeEntry, TimeSummary } from '../time-tracking-api.service';
import { parseTimeFilters } from './time-filters';
import { TimeEntriesApiActions, TimeEntriesPageActions, TimeReportApiActions, TimeReportPageActions } from './time-tracking.actions';

export interface TimeTrackingState extends EntityState<TimeEntry> {
  projectId: string | null;
  /** The task whose entries are loaded. */
  taskId: string | null;
  estimatedMinutes: number | null;
  loading: boolean;
  loadError: ApiError | null;
  /** The project report. */
  report: TimeSummary | null;
  reportProjectId: string | null;
  reportLoading: boolean;
  reportError: ApiError | null;
  exporting: boolean;
}

/** Newest date first; entries of one day, newest created first. */
const adapter = createEntityAdapter<TimeEntry>({
  sortComparer: (a, b) => b.date.localeCompare(a.date) || b.createdAt.localeCompare(a.createdAt),
});

export const initialTimeTrackingState: TimeTrackingState = adapter.getInitialState({
  projectId: null,
  taskId: null,
  estimatedMinutes: null,
  loading: false,
  loadError: null,
  report: null,
  reportProjectId: null,
  reportLoading: false,
  reportError: null,
  exporting: false,
});

const { selectQueryParams } = getRouterSelectors();

export const timeTrackingFeature = createFeature({
  name: 'timeTracking',
  reducer: createReducer(
    initialTimeTrackingState,
    on(
      ShellActions.logoutClicked,
      AuthApiActions.sessionExpired,
      TenantActions.tenantSwitched,
      (): TimeTrackingState => initialTimeTrackingState,
    ),
    on(TimeEntriesPageActions.opened, (state, { projectId, taskId }): TimeTrackingState =>
      state.taskId === taskId
        ? { ...state, loading: true, loadError: null }
        : adapter.removeAll({ ...state, projectId, taskId, estimatedMinutes: null, loading: true, loadError: null }),
    ),
    on(TimeEntriesApiActions.taskTimeLoaded, (state, { taskId, time }): TimeTrackingState =>
      taskId === state.taskId
        ? adapter.setAll([...time.entries], { ...state, estimatedMinutes: time.estimatedMinutes, loading: false })
        : state,
    ),
    on(TimeEntriesApiActions.taskTimeLoadFailed, (state, { error }): TimeTrackingState => ({ ...state, loading: false, loadError: error })),
    on(TimeEntriesApiActions.entrySaved, (state, { entry }): TimeTrackingState =>
      entry.taskId === state.taskId ? adapter.upsertOne(entry, state) : state,
    ),
    on(TimeEntriesApiActions.entryDeleted, (state, { entryId }): TimeTrackingState => adapter.removeOne(entryId, state)),
    on(TimeReportPageActions.opened, (state, { projectId }): TimeTrackingState => ({
      ...state,
      // Another project's report must not flash while this one loads.
      report: state.reportProjectId === projectId ? state.report : null,
      reportProjectId: projectId,
      reportLoading: true,
      reportError: null,
    })),
    on(TimeReportApiActions.summaryLoaded, (state, { projectId, summary }): TimeTrackingState =>
      projectId === state.reportProjectId ? { ...state, report: summary, reportLoading: false } : state,
    ),
    on(TimeReportApiActions.summaryLoadFailed, (state, { error }): TimeTrackingState => ({
      ...state,
      reportLoading: false,
      reportError: error,
    })),
    on(TimeReportPageActions.exportClicked, (state): TimeTrackingState => ({ ...state, exporting: true })),
    on(TimeReportApiActions.exportDone, TimeReportApiActions.exportFailed, (state): TimeTrackingState => ({ ...state, exporting: false })),
  ),
  extraSelectors: ({ selectTimeTrackingState }) => {
    const { selectAll } = adapter.getSelectors(selectTimeTrackingState);
    return {
      selectEntries: selectAll,
      selectTotalMinutes: createSelector(selectAll, (entries) => entries.reduce((sum, entry) => sum + entry.minutes, 0)),
      selectFilters: createSelector(selectQueryParams, (params) => parseTimeFilters(params ?? {})),
    };
  },
});
