import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../auth/store/auth.actions';
import { authFeature } from '../../auth/store/auth.feature';
import { Tenant } from '../tenant.models';
import { TenantActions, TenantApiActions } from './tenant.actions';

/** `loading` from sign-in until the user's tenants are known; guards wait for it so the first calls carry a tenant. */
export type TenantStatus = 'idle' | 'loading' | 'ready' | 'error';

export interface TenantState {
  tenants: Tenant[];
  currentId: string | null;
  status: TenantStatus;
}

export const initialTenantState: TenantState = { tenants: [], currentId: null, status: 'idle' };

/** The remembered tenant if the user still belongs to it, else the first one. */
export function pickTenant(tenants: readonly Tenant[], preferredId: string | null): string | null {
  return (tenants.find((tenant) => tenant.id === preferredId) ?? tenants[0])?.id ?? null;
}

export const tenantFeature = createFeature({
  name: 'tenant',
  reducer: createReducer(
    initialTenantState,
    on(AuthApiActions.sessionRestored, AuthApiActions.loginSucceeded, (): TenantState => ({
      ...initialTenantState,
      status: 'loading',
    })),
    on(TenantApiActions.tenantsLoaded, (_state, { tenants, preferredId }): TenantState => ({
      tenants,
      currentId: pickTenant(tenants, preferredId),
      status: 'ready',
    })),
    on(TenantApiActions.tenantsLoadFailed, (): TenantState => ({
      ...initialTenantState,
      status: 'error',
    })),
    on(TenantActions.retryClicked, (state): TenantState =>
      state.status === 'error' ? { ...state, status: 'loading' } : state,
    ),
    on(TenantActions.tenantSwitched, (state, { tenantId }): TenantState => {
      return state.tenants.some((tenant) => tenant.id === tenantId)
        ? { ...state, currentId: tenantId }
        : state;
    }),
    on(
      ShellActions.logoutClicked,
      AuthApiActions.sessionExpired,
      AuthApiActions.noSession,
      (): TenantState => initialTenantState,
    ),
  ),
  extraSelectors: ({ selectTenants, selectCurrentId }) => {
    const selectCurrent = createSelector(
      selectTenants,
      selectCurrentId,
      (tenants, id) => tenants.find((t) => t.id === id) ?? null,
    );
    const selectRole = createSelector(selectCurrent, (tenant) => tenant?.role ?? null);
    return {
      selectCurrent,
      selectRole,
      selectHasSeveral: createSelector(selectTenants, (tenants) => tenants.length > 1),
      /** Owner or Admin of the current tenant; the super-admin counts as Owner everywhere, as in the API. */
      selectCanManage: createSelector(
        selectRole,
        authFeature.selectIsAdmin,
        (role, platformAdmin) => platformAdmin || role === 'Owner' || role === 'Admin',
      ),
      selectIsOwner: createSelector(
        selectRole,
        authFeature.selectIsAdmin,
        (role, platformAdmin) => platformAdmin || role === 'Owner',
      ),
    };
  },
});
