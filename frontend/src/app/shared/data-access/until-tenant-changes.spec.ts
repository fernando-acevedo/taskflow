import { Action } from '@ngrx/store';
import { Subject } from 'rxjs';
import { AuthApiActions, ShellActions } from '../../core/auth/store/auth.actions';
import { TenantActions } from '../../core/tenant/store/tenant.actions';
import { untilTenantChanges } from './until-tenant-changes';

describe('untilTenantChanges', () => {
  const cancelling: [string, Action][] = [
    ['a tenant switch', TenantActions.tenantSwitched({ tenantId: 't' })],
    ['sign-out', ShellActions.logoutClicked()],
    ['an expired session', AuthApiActions.sessionExpired()],
  ];

  it.each(cancelling)('drops an answer that arrives after %s', (_name, action) => {
    const actions$ = new Subject<Action>();
    const response$ = new Subject<string>();
    const received: string[] = [];
    response$.pipe(untilTenantChanges(actions$)).subscribe((v) => received.push(v));

    response$.next('before');
    actions$.next(action);
    response$.next('after');

    expect(received).toEqual(['before']);
  });

  it('ignores unrelated actions', () => {
    const actions$ = new Subject<Action>();
    const response$ = new Subject<string>();
    const received: string[] = [];
    response$.pipe(untilTenantChanges(actions$)).subscribe((v) => received.push(v));

    actions$.next({ type: 'Something else' });
    response$.next('kept');

    expect(received).toEqual(['kept']);
  });
});
