import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as tasksEffects from '../tasks/data-access/store/tasks.effects';
import { tasksFeature } from '../tasks/data-access/store/tasks.feature';
import * as projectsEffects from './data-access/store/projects.effects';
import { projectsFeature } from './data-access/store/projects.feature';

/** Projects and, inside an open project, its tasks. Both stores live as long as the user stays under /projects. */
export const projectsRoutes: Routes = [
  {
    path: '',
    providers: [
      provideState(projectsFeature),
      provideEffects(projectsEffects),
      provideState(tasksFeature),
      provideEffects(tasksEffects),
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
          { path: 'members', loadComponent: () => import('./pages/members-page').then((m) => m.MembersPage) },
          { path: 'settings', loadComponent: () => import('./pages/settings-page').then((m) => m.SettingsPage) },
        ],
      },
    ],
  },
];
