import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as adminUsersEffects from './data-access/store/admin-users.effects';
import { adminUsersFeature } from './data-access/store/admin-users.feature';

export const adminRoutes: Routes = [
  {
    path: 'users',
    providers: [provideState(adminUsersFeature), provideEffects(adminUsersEffects)],
    loadComponent: () => import('./pages/users-page').then((m) => m.UsersPage),
  },
];
