import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { ADMIN_ROLE, LoginErrorCode, User } from '../auth.models';
import {
  AuthApiActions,
  InvitationPageActions,
  LoginPageActions,
  ProfilePageActions,
  RegisterPageActions,
  ShellActions,
} from './auth.actions';

/** `unknown` only until the startup session check finishes. */
export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous';

export interface AuthState {
  user: User | null;
  status: SessionStatus;
  pending: boolean;
  loginError: LoginErrorCode | null;
  /** Error codes for the register, invitation and profile forms (Identity codes, field names or generic codes). */
  formErrors: string[];
  profileSaved: boolean;
}

export const initialAuthState: AuthState = {
  user: null,
  status: 'unknown',
  pending: false,
  loginError: null,
  formErrors: [],
  profileSaved: false,
};

const signedOut: AuthState = { ...initialAuthState, status: 'anonymous' };

export const authFeature = createFeature({
  name: 'auth',
  reducer: createReducer(
    initialAuthState,
    on(
      LoginPageActions.opened,
      RegisterPageActions.opened,
      InvitationPageActions.opened,
      ProfilePageActions.opened,
      (state): AuthState => ({ ...state, loginError: null, formErrors: [], profileSaved: false }),
    ),
    on(
      LoginPageActions.loginSubmitted,
      RegisterPageActions.registerSubmitted,
      InvitationPageActions.passwordSubmitted,
      ProfilePageActions.displayNameSubmitted,
      (state): AuthState => ({ ...state, pending: true, loginError: null, formErrors: [], profileSaved: false }),
    ),
    on(
      AuthApiActions.sessionRestored,
      AuthApiActions.loginSucceeded,
      (state, { user }): AuthState => ({ ...state, user, status: 'authenticated', pending: false }),
    ),
    on(AuthApiActions.noSession, AuthApiActions.sessionExpired, ShellActions.logoutClicked, (): AuthState => signedOut),
    on(AuthApiActions.loginFailed, (state, { error }): AuthState => ({ ...state, pending: false, loginError: error })),
    on(
      AuthApiActions.registerFailed,
      AuthApiActions.invitationAcceptFailed,
      AuthApiActions.profileUpdateFailed,
      (state, { errors }): AuthState => ({ ...state, pending: false, formErrors: errors }),
    ),
    on(
      AuthApiActions.profileUpdated,
      (state, { displayName }): AuthState => ({
        ...state,
        pending: false,
        profileSaved: true,
        user: state.user ? { ...state.user, displayName } : null,
      }),
    ),
  ),
  extraSelectors: ({ selectStatus, selectUser }) => ({
    selectIsAuthenticated: createSelector(selectStatus, (status) => status === 'authenticated'),
    selectIsAdmin: createSelector(selectUser, (user) => user?.roles.includes(ADMIN_ROLE) ?? false),
  }),
});
