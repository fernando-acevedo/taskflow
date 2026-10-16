import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { HealthReport, SystemInfo } from '../status-api.service';
import { StatusApiActions, StatusPageActions } from './status.actions';

export interface StatusState {
  health: HealthReport | null;
  info: SystemInfo | null;
  loading: boolean;
  error: string | null;
  checkedAt: string | null;
}

export const initialStatusState: StatusState = {
  health: null,
  info: null,
  loading: false,
  error: null,
  checkedAt: null,
};

export const statusFeature = createFeature({
  name: 'status',
  reducer: createReducer(
    initialStatusState,
    on(StatusPageActions.statusRequested, (state): StatusState => ({ ...state, loading: true, error: null })),
    on(StatusApiActions.statusLoaded, (state, { health, info, checkedAt }): StatusState => ({
      ...state,
      health,
      info,
      checkedAt,
      loading: false,
    })),
    on(StatusApiActions.statusLoadFailed, (state, { error, checkedAt }): StatusState => ({
      ...state,
      health: null,
      info: null,
      error,
      checkedAt,
      loading: false,
    })),
  ),
  extraSelectors: ({ selectHealth, selectError }) => ({
    /** Healthy, Degraded or Unhealthy from the API, or Unreachable when the call itself failed. */
    selectOverallStatus: createSelector(selectHealth, selectError, (health, error) =>
      error ? 'Unreachable' : (health?.status ?? null),
    ),
  }),
});
