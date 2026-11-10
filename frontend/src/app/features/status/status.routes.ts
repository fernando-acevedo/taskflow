import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as statusEffects from './data-access/store/status.effects';
import { statusFeature } from './data-access/store/status.feature';

export const statusRoutes: Routes = [
  {
    path: '',
    title: 'titles.status',
    loadComponent: () => import('./pages/status-page').then((m) => m.StatusPage),
    providers: [provideState(statusFeature), provideEffects(statusEffects)],
  },
];
