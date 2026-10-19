import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { ApiError } from '../../../../core/http/api-error';
import { ProjectDashboard } from '../dashboard-api.service';
import { DashboardApiActions, ProjectOverviewActions } from './dashboard.actions';

export interface ProjectDashboardState {
  projectId: string | null;
  dashboard: ProjectDashboard | null;
  loading: boolean;
  error: ApiError | null;
}

export const initialProjectDashboardState: ProjectDashboardState = {
  projectId: null,
  dashboard: null,
  loading: false,
  error: null,
};

export const projectDashboardFeature = createFeature({
  name: 'projectDashboard',
  reducer: createReducer(
    initialProjectDashboardState,
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): ProjectDashboardState => initialProjectDashboardState),
    on(ProjectOverviewActions.opened, (state, { projectId }): ProjectDashboardState => ({
      projectId,
      // Another project's figures must not show while this one loads.
      dashboard: state.projectId === projectId ? state.dashboard : null,
      loading: true,
      error: null,
    })),
    on(DashboardApiActions.projectDashboardLoaded, (state, { dashboard }): ProjectDashboardState =>
      dashboard.projectId === state.projectId ? { ...state, dashboard, loading: false } : state,
    ),
    on(DashboardApiActions.projectDashboardLoadFailed, (state, { projectId, error }): ProjectDashboardState =>
      projectId === state.projectId ? { ...state, loading: false, error } : state,
    ),
  ),
});
