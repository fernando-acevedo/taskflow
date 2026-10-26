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
    path: 'forgot-password',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/pages/forgot-password-page').then((m) => m.ForgotPasswordPage),
  },
  {
    // No guard: registering signs the user in, so the link is usually opened while signed in.
    path: 'confirm-email',
    loadComponent: () => import('./features/auth/pages/confirm-email-page').then((m) => m.ConfirmEmailPage),
  },
  {
    // No guard, like accept-invitation: the link may be opened in a browser where someone else is signed in.
    path: 'reset-password',
    loadComponent: () => import('./features/auth/pages/reset-password-page').then((m) => m.ResetPasswordPage),
  },
  {
    // No guard: the invited user may open the link in a browser where someone else is signed in.
    path: 'accept-invitation',
    loadComponent: () => import('./features/auth/pages/accept-invitation-page').then((m) => m.AcceptInvitationPage),
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
