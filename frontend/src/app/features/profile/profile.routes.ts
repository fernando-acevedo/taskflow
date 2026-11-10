import { Routes } from '@angular/router';
import { provideEffects } from '@ngrx/effects';
import { provideState } from '@ngrx/store';
import { lazyUnsavedChangesGuard } from '@core/navigation/lazy-unsaved-changes-guard';
import { apiKeysEffects, apiKeysFeature } from '@features/api-keys';

export const profileRoutes: Routes = [
  {
    path: '',
    title: 'titles.profile',
    canDeactivate: [lazyUnsavedChangesGuard],
    providers: [provideState(apiKeysFeature), provideEffects(apiKeysEffects)],
    loadComponent: () => import('./pages/profile-page').then((m) => m.ProfilePage),
  },
];
