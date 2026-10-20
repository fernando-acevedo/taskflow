import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as projectDashboardEffects from '../dashboard/data-access/store/project-dashboard.effects';
import { projectDashboardFeature } from '../dashboard/data-access/store/project-dashboard.feature';
import * as tasksEffects from '../tasks/data-access/store/tasks.effects';
import { tasksFeature } from '../tasks/data-access/store/tasks.feature';
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
      provideState(projectDashboardFeature),
      provideEffects(projectDashboardEffects),
    ],
    children: [
      { path: '', loadComponent: () => import('./pages/projects-page').then((m) => m.ProjectsPage) },
      {
        path: ':projectId',
        loadComponent: () => import('./pages/project-page').then((m) => m.ProjectPage),
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'board' },
          { path: 'board', loadComponent: () => import('../tasks/pages/board-page').then((m) => m.BoardPage) },
          { path: 'list', loadComponent: () => import('../tasks/pages/task-list-page').then((m) => m.TaskListPage) },
          {
            path: 'overview',
            loadComponent: () => import('../dashboard/pages/project-overview-page').then((m) => m.ProjectOverviewPage),
          },
          { path: 'members', loadComponent: () => import('./pages/members-page').then((m) => m.MembersPage) },
          { path: 'settings', loadComponent: () => import('./pages/settings-page').then((m) => m.SettingsPage) },
        ],
      },
    ],
  },
];
