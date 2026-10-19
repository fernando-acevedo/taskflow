import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { ApiError } from '../../../../core/http/api-error';
import { MyDashboard } from '../dashboard-api.service';
import { DashboardApiActions, DashboardPageActions } from './dashboard.actions';

/** The home page's numbers. Kept while reloading, so returning home shows the last figures at once. */
export interface MyDashboardState {
  dashboard: MyDashboard | null;
  loading: boolean;
  error: ApiError | null;
}

export const initialMyDashboardState: MyDashboardState = { dashboard: null, loading: false, error: null };

export const myDashboardFeature = createFeature({
  name: 'myDashboard',
  reducer: createReducer(
    initialMyDashboardState,
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): MyDashboardState => initialMyDashboardState),
    on(DashboardPageActions.opened, (state): MyDashboardState => ({ ...state, loading: true, error: null })),
    on(DashboardApiActions.myDashboardLoaded, (state, { dashboard }): MyDashboardState => ({ ...state, dashboard, loading: false })),
    on(DashboardApiActions.myDashboardLoadFailed, (state, { error }): MyDashboardState => ({ ...state, loading: false, error })),
  ),
});
