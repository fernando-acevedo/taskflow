import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import * as clientsEffects from './data-access/store/clients.effects';
import { clientsFeature } from './data-access/store/clients.feature';

export const clientsRoutes: Routes = [
  {
    path: '',
    providers: [provideState(clientsFeature), provideEffects(clientsEffects)],
    loadComponent: () => import('./pages/clients-page').then((m) => m.ClientsPage),
  },
];
