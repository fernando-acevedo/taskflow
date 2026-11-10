import { Routes } from '@angular/router';
import { authGuard, guestGuard, tenantAdminGuard } from '@core/auth/auth.guards';

// Features are reached only through their public API (`index.ts`), loaded on demand: the barrels re-export the
// routes (or lazy page loaders), so nothing of a feature lands in the initial bundle.
export const routes: Routes = [
  {
    path: 'login',
    title: 'titles.login',
    canActivate: [guestGuard],
    loadComponent: () => import('@features/auth').then((m) => m.loadLoginPage()),
  },
  {
    path: 'register',
    title: 'titles.register',
    canActivate: [guestGuard],
    loadComponent: () => import('@features/auth').then((m) => m.loadRegisterPage()),
  },
  {
    path: 'forgot-password',
    title: 'titles.forgotPassword',
    canActivate: [guestGuard],
    loadComponent: () => import('@features/auth').then((m) => m.loadForgotPasswordPage()),
  },
  {
    // No guard: registering signs the user in, so the link is usually opened while signed in.
    path: 'confirm-email',
    title: 'titles.confirmEmail',
    loadComponent: () => import('@features/auth').then((m) => m.loadConfirmEmailPage()),
  },
  {
    // No guard, like accept-invitation: the link may be opened in a browser where someone else is signed in.
    path: 'reset-password',
    title: 'titles.resetPassword',
    loadComponent: () => import('@features/auth').then((m) => m.loadResetPasswordPage()),
  },
  {
    // No guard: the invited user may open the link in a browser where someone else is signed in.
    path: 'accept-invitation',
    title: 'titles.acceptInvitation',
    loadComponent: () => import('@features/auth').then((m) => m.loadAcceptInvitationPage()),
  },
  {
    path: 'status',
    loadChildren: () => import('@features/status').then((m) => m.loadStatusRoutes()),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadChildren: () => import('@features/dashboard').then((m) => m.loadDashboardRoutes()),
      },
      {
        path: 'profile',
        loadChildren: () => import('@features/profile').then((m) => m.loadProfileRoutes()),
      },
      {
        path: 'projects',
        loadChildren: () => import('@features/projects').then((m) => m.loadProjectsRoutes()),
      },
      {
        path: 'clients',
        canActivate: [tenantAdminGuard],
        loadChildren: () => import('@features/clients').then((m) => m.loadClientsRoutes()),
      },
      {
        path: 'admin',
        loadChildren: () => import('@features/admin').then((m) => m.loadAdminRoutes()),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
