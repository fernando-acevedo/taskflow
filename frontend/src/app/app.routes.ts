import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as statusEffects from './features/status/data-access/store/status.effects';
import { statusFeature } from './features/status/data-access/store/status.feature';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/status/pages/status-page').then((m) => m.StatusPage),
    providers: [provideState(statusFeature), provideEffects(statusEffects)],
  },
  { path: '**', redirectTo: '' },
];
