import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';
import * as statusEffects from './features/status/data-access/store/status.effects';
import { statusFeature } from './features/status/data-access/store/status.feature';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/pages/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/pages/register-page').then((m) => m.RegisterPage),
  },
  {
    path: 'status',
    loadComponent: () => import('./features/status/pages/status-page').then((m) => m.StatusPage),
    providers: [provideState(statusFeature), provideEffects(statusEffects)],
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadChildren: () => import('./features/dashboard/dashboard.routes').then((m) => m.dashboardRoutes),
      },
      {
        path: 'profile',
        loadComponent: () => import('./features/profile/pages/profile-page').then((m) => m.ProfilePage),
      },
      {
        path: 'projects',
        loadChildren: () => import('./features/projects/projects.routes').then((m) => m.projectsRoutes),
      },
      {
        path: 'clients',
        canActivate: [adminGuard],
        loadChildren: () => import('./features/clients/clients.routes').then((m) => m.clientsRoutes),
      },
      {
        path: 'admin',
        canActivate: [adminGuard],
        loadChildren: () => import('./features/admin/admin.routes').then((m) => m.adminRoutes),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
