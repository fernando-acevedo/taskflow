import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import { authGuard, guestGuard } from './core/auth/auth.guards';
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
        loadComponent: () => import('./features/home/pages/home-page').then((m) => m.HomePage),
      },
      {
        path: 'profile',
        loadComponent: () => import('./features/profile/pages/profile-page').then((m) => m.ProfilePage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
