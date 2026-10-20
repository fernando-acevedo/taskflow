import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as myDashboardEffects from './data-access/store/my-dashboard.effects';
import { myDashboardFeature } from './data-access/store/my-dashboard.feature';

/** The home page. The project overview tab lives under the projects routes, with its own state. */
export const dashboardRoutes: Routes = [
  {
    path: '',
    providers: [provideState(myDashboardFeature), provideEffects(myDashboardEffects)],
    loadComponent: () => import('./pages/dashboard-page').then((m) => m.DashboardPage),
  },
];
