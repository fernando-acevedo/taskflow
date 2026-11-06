import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { AuthApiActions, ShellActions } from '../../auth/store/auth.actions';
import { authFeature } from '../../auth/store/auth.feature';
import { DemoService } from '../../demo/demo.service';
import { TenantApiService } from '../tenant-api.service';
import { TenantStorage } from '../tenant-storage';
import { Tenant } from '../tenant.models';
import { TenantActions, TenantApiActions } from './tenant.actions';
import { goHomeOnSwitch, loadTenants, rememberTenant } from './tenant.effects';

const acme: Tenant = { id: 'a', name: 'Acme', slug: 'acme', isDemo: false, role: 'Owner' };
const user = {
  id: 'u1',
  email: 'ada@tests.local',
  displayName: 'Ada',
  isActive: true,
  roles: [],
  isInvitationPending: false,
  isEmailConfirmed: true,
};

describe('tenant effects', () => {
  let storage: TenantStorage;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideMockStore({ selectors: [{ selector: authFeature.selectUser, value: user }] }),
      ],
    });
    storage = TestBed.inject(TenantStorage);
  });

  afterEach(() => localStorage.clear());

  function run(demoTenantId: string | null, mine: TenantApiService['mine']): Action[] {
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    const demo = {
      status: signal({ enabled: true, nextResetAt: null, accounts: [], tenantId: demoTenantId }),
    } as unknown as DemoService;
    TestBed.runInInjectionContext(() =>
      loadTenants(
        actions$ as unknown as Actions,
        { mine } as unknown as TenantApiService,
        storage,
        demo,
      ).subscribe((a) => emitted.push(a)),
    );
    actions$.next(AuthApiActions.sessionRestored({ user }));
    return emitted;
  }

  it('hands the remembered tenant of this user to the reducer', () => {
    storage.save('u1', 'a');

    expect(run(null, () => of([acme]))).toEqual([
      TenantApiActions.tenantsLoaded({ tenants: [acme], preferredId: 'a' }),
    ]);
  });

  it('prefers the demo tenant when nothing is remembered, and nothing otherwise', () => {
    expect(run('demo-id', () => of([acme]))).toEqual([
      TenantApiActions.tenantsLoaded({ tenants: [acme], preferredId: 'demo-id' }),
    ]);
    expect(run(null, () => of([acme]))).toEqual([
      TenantApiActions.tenantsLoaded({ tenants: [acme], preferredId: null }),
    ]);
  });

  it('reports a failed call as an error, not as an empty list', () => {
    expect(run(null, () => throwError(() => new Error('down')))).toEqual([
      TenantApiActions.tenantsLoadFailed({ error: { code: 'unexpected', fields: {} } }),
    ]);
  });

  it('drops the answer that arrives after sign-out', () => {
    const response$ = new Subject<Tenant[]>();
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    const demo = { status: signal({ tenantId: null }) } as unknown as DemoService;
    TestBed.runInInjectionContext(() =>
      loadTenants(
        actions$ as unknown as Actions,
        { mine: () => response$ } as unknown as TenantApiService,
        storage,
        demo,
      ).subscribe((a) => emitted.push(a)),
    );

    actions$.next(AuthApiActions.sessionRestored({ user }));
    actions$.next(ShellActions.logoutClicked());
    response$.next([acme]);

    expect(emitted).toEqual([]);
  });

  it('asks again on retry', () => {
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    const demo = { status: signal({ tenantId: null }) } as unknown as DemoService;
    TestBed.runInInjectionContext(() =>
      loadTenants(
        actions$ as unknown as Actions,
        { mine: () => of([acme]) } as unknown as TenantApiService,
        storage,
        demo,
      ).subscribe((a) => emitted.push(a)),
    );

    actions$.next(TenantActions.retryClicked());

    expect(emitted).toEqual([TenantApiActions.tenantsLoaded({ tenants: [acme], preferredId: null })]);
  });

  it('remembers a switch for the signed-in user, and goes home', () => {
    const actions$ = new Subject<Action>();
    const navigateByUrl = vi.fn();
    TestBed.runInInjectionContext(() => {
      rememberTenant(
        actions$ as Actions,
        TestBed.inject<MockStore>(MockStore),
        storage,
      ).subscribe();
      goHomeOnSwitch(actions$ as Actions, { navigateByUrl } as unknown as Router).subscribe();
    });

    actions$.next(TenantActions.tenantSwitched({ tenantId: 'g' }));

    expect(storage.read('u1')).toBe('g');
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });
});
