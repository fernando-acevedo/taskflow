import { Injector, inject, runInInjectionContext } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { firstValueFrom, isObservable } from 'rxjs';
import type { HasUnsavedChanges } from '@shared/ui/unsaved-changes';
import { Notifier } from '../notifications/notifier';

type GuardModule = Pick<typeof import('@shared/ui/unsaved-changes'), 'unsavedChangesGuard'>;

/**
 * `unsavedChangesGuard` loaded on demand: the dialog code behind it is downloaded only when a modified form is about
 * to be left. When that download fails the navigation goes ahead (the user is told, and the guard is just a courtesy:
 * blocking would trap them on the page) instead of staying on a rejected promise.
 */
export function createLazyUnsavedChangesGuard(
  load: () => Promise<GuardModule> = () => import('@shared/ui/unsaved-changes'),
): CanDeactivateFn<HasUnsavedChanges> {
  return async (page, ...rest) => {
    if (!page.hasUnsavedChanges()) {
      return true;
    }
    const injector = inject(Injector);
    try {
      const module = await load();
      const result = runInInjectionContext(injector, () => module.unsavedChangesGuard(page, ...rest));
      return isObservable(result) ? await firstValueFrom(result, { defaultValue: false }) : await result;
    } catch {
      injector.get(Notifier).warning('common.unsaved.loadFailed');
      return true;
    }
  };
}

export const lazyUnsavedChangesGuard = createLazyUnsavedChangesGuard();
