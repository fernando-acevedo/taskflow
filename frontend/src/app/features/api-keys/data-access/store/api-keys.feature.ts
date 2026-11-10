import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { ApiError } from '@shared/data-access/api-error';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { ApiKey } from '../api-keys-api.service';
import { ApiKeysApiActions, ApiKeysPageActions } from './api-keys.actions';

export interface ApiKeysState extends EntityState<ApiKey> {
  loading: boolean;
  loadError: ApiError | null;
}

const adapter = createEntityAdapter<ApiKey>();

export const initialApiKeysState: ApiKeysState = adapter.getInitialState({
  loading: false,
  loadError: null,
});

// The state holds the keys' prefixes only; a created key's secret is never stored.
export const apiKeysFeature = createFeature({
  name: 'apiKeys',
  reducer: createReducer(
    initialApiKeysState,
    on(
      ShellActions.logoutClicked,
      AuthApiActions.sessionExpired,
      TenantActions.tenantSwitched,
      (): ApiKeysState => initialApiKeysState,
    ),
    on(ApiKeysPageActions.opened, (state): ApiKeysState => ({ ...state, loading: true, loadError: null })),
    on(ApiKeysApiActions.keysLoaded, (state, { keys }): ApiKeysState =>
      adapter.setAll(keys, { ...state, loading: false }),
    ),
    on(ApiKeysApiActions.keysLoadFailed, (state, { error }): ApiKeysState => ({
      ...state,
      loading: false,
      loadError: error,
    })),
  ),
  extraSelectors: ({ selectApiKeysState }) => adapter.getSelectors(selectApiKeysState),
});
