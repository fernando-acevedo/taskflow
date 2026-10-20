import { ProjectDashboard } from '../dashboard-api.service';
import { DashboardApiActions, ProjectOverviewActions } from './dashboard.actions';
import { initialProjectDashboardState, projectDashboardFeature } from './project-dashboard.feature';

const dashboard = (projectId: string): ProjectDashboard => ({
  projectId,
  totalTasks: 1,
  openTasks: 1,
  overdueTasks: 0,
  dueThisWeek: 0,
  byStatus: [{ status: 'ToDo', count: 1 }],
  openByPriority: [{ priority: 'Medium', count: 1 }],
  openByAssignee: [{ userId: null, displayName: null, openTasks: 1, overdueTasks: 0 }],
});

describe('projectDashboardFeature', () => {
  const { reducer } = projectDashboardFeature;

  it('keeps the figures while the same project reloads, and drops them for another project', () => {
    const loaded = reducer(
      reducer(initialProjectDashboardState, ProjectOverviewActions.opened({ projectId: 'p1' })),
      DashboardApiActions.projectDashboardLoaded({ dashboard: dashboard('p1') }),
    );

    expect(reducer(loaded, ProjectOverviewActions.opened({ projectId: 'p1' })).dashboard?.projectId).toBe('p1');
    expect(reducer(loaded, ProjectOverviewActions.opened({ projectId: 'p2' })).dashboard).toBeNull();
  });

  it('ignores a late answer for a project that is no longer open', () => {
    const opened = reducer(initialProjectDashboardState, ProjectOverviewActions.opened({ projectId: 'p2' }));

    const state = reducer(opened, DashboardApiActions.projectDashboardLoaded({ dashboard: dashboard('p1') }));

    expect(state.dashboard).toBeNull();
    expect(state.loading).toBe(true);
  });
});
