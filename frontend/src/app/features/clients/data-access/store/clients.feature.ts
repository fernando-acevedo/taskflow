import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { ApiError } from '../../../../core/http/api-error';
import { Client, ClientQuery } from '../clients-api.service';
import { ClientsApiActions, ClientsPageActions } from './clients.actions';

export interface ClientsState extends EntityState<Client> {
  query: ClientQuery;
  totalCount: number;
  loading: boolean;
  saving: boolean;
  loadError: ApiError | null;
}

const adapter = createEntityAdapter<Client>();

export const initialClientsState: ClientsState = adapter.getInitialState({
  query: { search: '', includeArchived: false, page: 1, pageSize: 25 },
  totalCount: 0,
  loading: false,
  saving: false,
  loadError: null,
});

export const clientsFeature = createFeature({
  name: 'clients',
  reducer: createReducer(
    initialClientsState,
    // Another user may sign in next in this tab; nothing of the previous session may show.
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): ClientsState => initialClientsState),
    on(ClientsPageActions.opened, (state): ClientsState => ({ ...state, loading: true, loadError: null })),
    on(
      ClientsPageActions.queryChanged,
      (state, { query }): ClientsState => ({
        ...state,
        // A new search or filter starts again from the first page.
        query: { ...state.query, page: 1, ...query },
        loading: true,
        loadError: null,
      }),
    ),
    on(
      ClientsPageActions.createSubmitted,
      ClientsPageActions.updateSubmitted,
      ClientsPageActions.archiveConfirmed,
      ClientsPageActions.restoreClicked,
      (state): ClientsState => ({ ...state, saving: true }),
    ),
    on(
      ClientsApiActions.clientsLoaded,
      (state, { page }): ClientsState =>
        adapter.setAll(page.items, { ...state, totalCount: page.totalCount, loading: false }),
    ),
    on(ClientsApiActions.clientsLoadFailed, (state, { error }): ClientsState => ({ ...state, loading: false, loadError: error })),
    on(ClientsApiActions.clientSaved, (state): ClientsState => ({ ...state, saving: false, loading: true })),
    on(ClientsApiActions.clientSaveFailed, (state): ClientsState => ({ ...state, saving: false })),
  ),
  extraSelectors: ({ selectClientsState }) => adapter.getSelectors(selectClientsState),
});
