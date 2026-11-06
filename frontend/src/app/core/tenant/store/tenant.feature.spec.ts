import { AuthApiActions, ShellActions } from '../../auth/store/auth.actions';
import { Tenant } from '../tenant.models';
import { TenantActions, TenantApiActions } from './tenant.actions';
import { initialTenantState, pickTenant, tenantFeature } from './tenant.feature';

const acme: Tenant = { id: 'a', name: 'Acme', slug: 'acme', isDemo: false, role: 'Owner' };
const globex: Tenant = { id: 'g', name: 'Globex', slug: 'globex', isDemo: false, role: 'Member' };
const user = {
  id: 'u1',
  email: 'ada@tests.local',
  displayName: 'Ada',
  isActive: true,
  roles: [],
  isInvitationPending: false,
  isEmailConfirmed: true,
};

describe('tenant feature', () => {
  const { reducer } = tenantFeature;

  it('waits for the tenants after sign-in', () => {
    expect(
      reducer(initialTenantState, AuthApiActions.loginSucceeded({ user, returnUrl: '/' })).status,
    ).toBe('loading');
    expect(reducer(initialTenantState, AuthApiActions.sessionRestored({ user })).status).toBe(
      'loading',
    );
  });

  it('picks the remembered tenant while the user still belongs to it', () => {
    const state = reducer(
      initialTenantState,
      TenantApiActions.tenantsLoaded({ tenants: [acme, globex], preferredId: 'g' }),
    );

    expect(state).toEqual({ tenants: [acme, globex], currentId: 'g', status: 'ready' });
  });

  it.each([null, 'gone'])(
    'falls back to the first tenant when the remembered one is %s',
    (preferredId) => {
      expect(pickTenant([acme, globex], preferredId)).toBe('a');
    },
  );

  it('has no current tenant when the user has none', () => {
    const state = reducer(
      initialTenantState,
      TenantApiActions.tenantsLoaded({ tenants: [], preferredId: null }),
    );

    expect(state.currentId).toBeNull();
    expect(state.status).toBe('ready');
  });

  it('switches to a tenant of the user and ignores any other', () => {
    const loaded = reducer(
      initialTenantState,
      TenantApiActions.tenantsLoaded({ tenants: [acme, globex], preferredId: null }),
    );

    expect(reducer(loaded, TenantActions.tenantSwitched({ tenantId: 'g' })).currentId).toBe('g');
    expect(reducer(loaded, TenantActions.tenantSwitched({ tenantId: 'other' })).currentId).toBe(
      'a',
    );
  });

  it.each([ShellActions.logoutClicked(), AuthApiActions.sessionExpired()])(
    'is cleared on $type',
    (action) => {
      const loaded = reducer(
        initialTenantState,
        TenantApiActions.tenantsLoaded({ tenants: [acme], preferredId: null }),
      );

      expect(reducer(loaded, action)).toEqual(initialTenantState);
    },
  );

  describe('selectors', () => {
    const { selectCanManage, selectIsOwner } = tenantFeature;

    it('lets owners and admins manage, and only owners own', () => {
      expect(selectCanManage.projector('Owner', false)).toBe(true);
      expect(selectCanManage.projector('Admin', false)).toBe(true);
      expect(selectCanManage.projector('Member', false)).toBe(false);
      expect(selectCanManage.projector(null, false)).toBe(false);
      expect(selectIsOwner.projector('Admin', false)).toBe(false);
      expect(selectIsOwner.projector('Owner', false)).toBe(true);
    });

    it('counts the super-admin as an owner', () => {
      expect(selectCanManage.projector(null, true)).toBe(true);
      expect(selectIsOwner.projector('Member', true)).toBe(true);
    });
  });
});
