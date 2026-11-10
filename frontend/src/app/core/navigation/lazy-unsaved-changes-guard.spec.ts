import { EnvironmentInjector, Injector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { of } from 'rxjs';
import { Notifier } from '../notifications/notifier';
import { createLazyUnsavedChangesGuard } from './lazy-unsaved-changes-guard';

describe('lazy unsaved changes guard', () => {
  const warning = vi.fn();

  beforeEach(() => {
    warning.mockReset();
    TestBed.configureTestingModule({ providers: [{ provide: Notifier, useValue: { warning } }] });
  });

  function run(guard: ReturnType<typeof createLazyUnsavedChangesGuard>, dirty: boolean) {
    const page = { hasUnsavedChanges: () => dirty };
    const route = {} as ActivatedRouteSnapshot;
    const state = {} as RouterStateSnapshot;
    return runInInjectionContext(TestBed.inject(EnvironmentInjector) as Injector, () =>
      guard(page, route, state, state),
    ) as Promise<boolean>;
  }

  it('does not load the module when nothing changed', async () => {
    const load = vi.fn();

    expect(await run(createLazyUnsavedChangesGuard(load), false)).toBe(true);
    expect(load).not.toHaveBeenCalled();
  });

  it('loads the module and uses its answer when the form is modified', async () => {
    const unsavedChangesGuard = vi.fn(() => of(false));

    const result = await run(createLazyUnsavedChangesGuard(() => Promise.resolve({ unsavedChangesGuard })), true);

    expect(result).toBe(false);
    expect(unsavedChangesGuard).toHaveBeenCalledOnce();
  });

  it('lets the navigation through and warns when the module fails to load', async () => {
    const result = await run(createLazyUnsavedChangesGuard(() => Promise.reject(new Error('chunk'))), true);

    expect(result).toBe(true);
    expect(warning).toHaveBeenCalledWith('common.unsaved.loadFailed');
  });
});
