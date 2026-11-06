import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import { adminGuard, tenantAdminGuard } from '../../core/auth/auth.guards';
import * as membersEffects from './data-access/store/members.effects';
import { membersFeature } from './data-access/store/members.feature';

export const adminRoutes: Routes = [
  {
    // The current tenant's people: its owners and admins.
    path: 'users',
    title: 'titles.users',
    canActivate: [tenantAdminGuard],
    providers: [provideState(membersFeature), provideEffects(membersEffects)],
    loadComponent: () => import('./pages/users-page').then((m) => m.UsersPage),
  },
  {
    // Platform view, for the super-admin only.
    path: 'tenants',
    title: 'titles.tenants',
    canActivate: [adminGuard],
    loadComponent: () => import('./pages/tenants-page').then((m) => m.TenantsPage),
  },
];
