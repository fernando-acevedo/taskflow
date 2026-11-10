import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import { lazyUnsavedChangesGuard } from '@core/navigation/lazy-unsaved-changes-guard';
import { activityEffects, activityFeature, loadActivityPage } from '@features/activity';
import { commentsEffects, commentsFeature } from '@features/comments';
import { projectDashboardEffects, projectDashboardFeature, loadProjectOverviewPage } from '@features/dashboard';
import { labelsEffects, labelsFeature } from '@features/labels';
import { TaskEditor, loadBoardPage, loadTaskListPage, tasksEffects, tasksFeature } from '@features/tasks';
import { loadTimeReportPage, timeTrackingEffects, timeTrackingFeature } from '@features/time-tracking';
import * as projectsEffects from './data-access/store/projects.effects';
import { projectsFeature } from './data-access/store/projects.feature';

/** Projects and, inside an open project, its tasks and metrics. The stores live as long as the user stays under /projects. */
export const projectsRoutes: Routes = [
  {
    path: '',
    providers: [
      provideState(projectsFeature),
      provideEffects(projectsEffects),
      provideState(tasksFeature),
      provideEffects(tasksEffects),
      provideState(commentsFeature),
      provideEffects(commentsEffects),
      provideState(activityFeature),
      provideEffects(activityEffects),
      provideState(labelsFeature),
      provideEffects(labelsEffects),
      provideState(timeTrackingFeature),
      provideEffects(timeTrackingEffects),
      provideState(projectDashboardFeature),
      provideEffects(projectDashboardEffects),
      TaskEditor,
    ],
    children: [
      { path: '', title: 'titles.projects', loadComponent: () => import('./pages/projects-page').then((m) => m.ProjectsPage) },
      {
        path: ':projectId',
        loadComponent: () => import('./pages/project-page').then((m) => m.ProjectPage),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'board' },
          { path: 'board', title: 'project.tabs.board', loadComponent: () => loadBoardPage() },
          { path: 'list', title: 'project.tabs.list', loadComponent: () => loadTaskListPage() },
          {
            path: 'overview',
            title: 'project.tabs.overview',
            loadComponent: () => loadProjectOverviewPage(),
          },
          { path: 'hours', title: 'project.tabs.hours', loadComponent: () => loadTimeReportPage() },
          { path: 'activity', title: 'project.tabs.activity', loadComponent: () => loadActivityPage() },
          { path: 'members', title: 'project.tabs.members', canDeactivate: [lazyUnsavedChangesGuard], loadComponent: () => import('./pages/members-page').then((m) => m.MembersPage) },
          { path: 'settings', title: 'project.tabs.settings', loadComponent: () => import('./pages/settings-page').then((m) => m.SettingsPage) },
        ],
      },
    ],
  },
];
