import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { ApiError } from '@shared/data-access/api-error';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { SupportAccess, TenantMember } from '../tenant-members-api.service';
import { MembersApiActions, MembersPageActions } from './members.actions';

export interface MembersState extends EntityState<TenantMember> {
  loading: boolean;
  saving: boolean;
  loadError: ApiError | null;
  supportAccess: SupportAccess[];
}

const adapter = createEntityAdapter<TenantMember>({ selectId: (member) => member.userId });

export const initialMembersState: MembersState = adapter.getInitialState({
  loading: false,
  saving: false,
  loadError: null,
  supportAccess: [],
});

export const membersFeature = createFeature({
  name: 'tenantMembers',
  reducer: createReducer(
    initialMembersState,
    // Another user, or another tenant, may come next in this tab; nothing of the previous one may show.
    on(
      ShellActions.logoutClicked,
      AuthApiActions.sessionExpired,
      TenantActions.tenantSwitched,
      (): MembersState => initialMembersState,
    ),
    on(MembersPageActions.opened, (state): MembersState => ({
      ...state,
      loading: true,
      loadError: null,
    })),
    on(
      MembersPageActions.addSubmitted,
      MembersPageActions.invitationResendClicked,
      MembersPageActions.roleChanged,
      MembersPageActions.removeClicked,
      MembersPageActions.activeChanged,
      (state): MembersState => ({ ...state, saving: true }),
    ),
    on(MembersApiActions.membersLoaded, (state, { members }): MembersState =>
      adapter.setAll(members, { ...state, loading: false }),
    ),
    on(MembersApiActions.membersLoadFailed, (state, { error }): MembersState => ({
      ...state,
      loading: false,
      loadError: error,
    })),
    on(
      MembersApiActions.memberAdded,
      MembersApiActions.memberUpdated,
      MembersApiActions.memberRemoved,
      (state): MembersState => ({ ...state, saving: false, loading: true }),
    ),
    on(
      MembersApiActions.invitationSent,
      MembersApiActions.memberUpdateFailed,
      (state): MembersState => ({ ...state, saving: false }),
    ),
    on(MembersApiActions.supportAccessLoaded, (state, { items }): MembersState => ({
      ...state,
      supportAccess: items,
    })),
  ),
  extraSelectors: ({ selectTenantMembersState }) => adapter.getSelectors(selectTenantMembersState),
});
