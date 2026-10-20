import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { User } from '../../../../core/auth/auth.models';
import { ApiError } from '../../../../core/http/api-error';
import { UserQuery } from '../admin-users-api.service';
import { AdminUsersApiActions, UsersPageActions } from './admin-users.actions';

export interface AdminUsersState extends EntityState<User> {
  query: UserQuery;
  totalCount: number;
  loading: boolean;
  saving: boolean;
  loadError: ApiError | null;
}

const adapter = createEntityAdapter<User>();

export const initialAdminUsersState: AdminUsersState = adapter.getInitialState({
  query: { search: '', page: 1, pageSize: 25 },
  totalCount: 0,
  loading: false,
  saving: false,
  loadError: null,
});

export const adminUsersFeature = createFeature({
  name: 'adminUsers',
  reducer: createReducer(
    initialAdminUsersState,
    // Another user may sign in next in this tab; nothing of the previous session may show.
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): AdminUsersState => initialAdminUsersState),
    on(UsersPageActions.opened, (state): AdminUsersState => ({ ...state, loading: true, loadError: null })),
    on(
      UsersPageActions.queryChanged,
      (state, { query }): AdminUsersState => ({
        ...state,
        query: { ...state.query, page: 1, ...query },
        loading: true,
        loadError: null,
      }),
    ),
    on(
      UsersPageActions.createSubmitted,
      UsersPageActions.invitationResendClicked,
      UsersPageActions.activeChanged,
      UsersPageActions.adminChanged,
      (state): AdminUsersState => ({ ...state, saving: true }),
    ),
    on(
      AdminUsersApiActions.usersLoaded,
      (state, { page }): AdminUsersState =>
        adapter.setAll([...page.items], { ...state, totalCount: page.totalCount, loading: false }),
    ),
    on(AdminUsersApiActions.usersLoadFailed, (state, { error }): AdminUsersState => ({ ...state, loading: false, loadError: error })),
    on(AdminUsersApiActions.userCreated, AdminUsersApiActions.userUpdated, (state): AdminUsersState => ({ ...state, saving: false, loading: true })),
    on(AdminUsersApiActions.invitationSent, AdminUsersApiActions.userUpdateFailed, (state): AdminUsersState => ({ ...state, saving: false })),
  ),
  extraSelectors: ({ selectAdminUsersState }) => adapter.getSelectors(selectAdminUsersState),
});
