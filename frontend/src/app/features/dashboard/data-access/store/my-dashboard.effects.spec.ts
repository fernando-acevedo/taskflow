import { Action } from '@ngrx/store';
import { Subject } from 'rxjs';
import { ShellActions } from '@core/auth/store/auth.actions';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { DashboardApiService, MyDashboard } from '../dashboard-api.service';
import { DashboardApiActions, DashboardPageActions } from './dashboard.actions';
import { loadMyDashboard } from './my-dashboard.effects';

describe('loadMyDashboard', () => {
  const dashboard = { totals: {} } as unknown as MyDashboard;

  function setup() {
    const actions$ = new Subject<Action>();
    const response$ = new Subject<MyDashboard>();
    const api = { mine: vi.fn(() => response$) } as unknown as DashboardApiService;
    const emitted: Action[] = [];
    loadMyDashboard(actions$, api).subscribe((a) => emitted.push(a));
    return { actions$, response$, emitted };
  }

  it('applies the answer', () => {
    const { actions$, response$, emitted } = setup();

    actions$.next(DashboardPageActions.opened());
    response$.next(dashboard);

    expect(emitted).toEqual([DashboardApiActions.myDashboardLoaded({ dashboard })]);
  });

  it.each([
    ['the tenant was switched', TenantActions.tenantSwitched({ tenantId: 'other' })],
    ['the user signed out', ShellActions.logoutClicked()],
  ])('drops the answer that arrives after %s', (_name, action) => {
    const { actions$, response$, emitted } = setup();

    actions$.next(DashboardPageActions.opened());
    actions$.next(action);
    response$.next(dashboard);

    expect(emitted).toEqual([]);
  });
});
