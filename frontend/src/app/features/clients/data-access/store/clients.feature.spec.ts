import { ShellActions } from '@core/auth/store/auth.actions';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { ClientsPageActions } from './clients.actions';
import { clientsFeature, initialClientsState } from './clients.feature';

describe('clientsFeature', () => {
  const { reducer } = clientsFeature;

  it('starts again from the first page on a new search, and loads', () => {
    const onPageThree = { ...initialClientsState, query: { ...initialClientsState.query, page: 3 } };

    const state = reducer(onPageThree, ClientsPageActions.queryChanged({ query: { search: 'acme' } }));

    expect(state.query).toEqual({ ...initialClientsState.query, search: 'acme', page: 1 });
    expect(state.loading).toBe(true);
  });

  it('keeps an explicit page of the change', () => {
    const state = reducer(initialClientsState, ClientsPageActions.queryChanged({ query: { page: 2 } }));

    expect(state.query.page).toBe(2);
  });

  it('forgets everything on sign-out and on a tenant switch', () => {
    const dirty = reducer(initialClientsState, ClientsPageActions.queryChanged({ query: { search: 'x' } }));

    expect(reducer(dirty, ShellActions.logoutClicked())).toEqual(initialClientsState);
    expect(reducer(dirty, TenantActions.tenantSwitched({ tenantId: 't' }))).toEqual(initialClientsState);
  });
});
