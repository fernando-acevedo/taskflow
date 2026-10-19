import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError } from '../../../../core/http/api-error';
import { MyDashboard, ProjectDashboard } from '../dashboard-api.service';

export const DashboardPageActions = createActionGroup({
  source: 'Dashboard Page',
  events: {
    Opened: emptyProps(),
  },
});

export const ProjectOverviewActions = createActionGroup({
  source: 'Project Overview',
  events: {
    Opened: props<{ projectId: string }>(),
  },
});

export const DashboardApiActions = createActionGroup({
  source: 'Dashboard API',
  events: {
    'My Dashboard Loaded': props<{ dashboard: MyDashboard }>(),
    'My Dashboard Load Failed': props<{ error: ApiError }>(),
    'Project Dashboard Loaded': props<{ dashboard: ProjectDashboard }>(),
    'Project Dashboard Load Failed': props<{ projectId: string; error: ApiError }>(),
  },
});
