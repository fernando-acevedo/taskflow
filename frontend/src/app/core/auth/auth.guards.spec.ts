import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Observable, firstValueFrom, isObservable } from 'rxjs';
import { tenantFeature } from '../tenant/store/tenant.feature';
import { tenantAdminGuard } from './auth.guards';

function run(
  canManage: boolean,
  status: 'loading' | 'ready' = 'ready',
): { result: Promise<boolean | UrlTree>; store: MockStore } {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideMockStore({
        selectors: [
          { selector: tenantFeature.selectStatus, value: status },
          { selector: tenantFeature.selectCanManage, value: canManage },
        ],
      }),
    ],
  });
  const outcome = TestBed.runInInjectionContext(() =>
    tenantAdminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );
  if (!isObservable(outcome)) {
    throw new Error('Expected an observable');
  }
  return {
    result: firstValueFrom(outcome as Observable<boolean | UrlTree>),
    store: TestBed.inject<MockStore>(MockStore),
  };
}

describe('tenantAdminGuard', () => {
  it('lets an owner or admin of the tenant in', async () => {
    expect(await run(true).result).toBe(true);
  });

  it('sends a member home', async () => {
    const result = await run(false).result;

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('waits until the tenants are known', async () => {
    const { result, store } = run(true, 'loading');
    let settled = false;
    void result.then(() => (settled = true));
    await Promise.resolve();
    expect(settled).toBe(false);

    store.overrideSelector(tenantFeature.selectStatus, 'ready');
    store.refreshState();

    expect(await result).toBe(true);
  });
});
