import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { ADMIN_ROLE, EmailConfirmationStatus, LoginErrorCode, User } from '../auth.models';
import {
  AuthApiActions,
  ConfirmEmailPageActions,
  ForgotPasswordPageActions,
  InvitationPageActions,
  LoginPageActions,
  ProfilePageActions,
  RegisterPageActions,
  ResetPasswordPageActions,
  ShellActions,
} from './auth.actions';

/** `unknown` only until the startup session check finishes. */
export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous';

export interface AuthState {
  user: User | null;
  status: SessionStatus;
  pending: boolean;
  loginError: LoginErrorCode | null;
  /** Error codes for the register, invitation, password and profile forms (Identity codes, field names or generic codes). */
  formErrors: string[];
  profileSaved: boolean;
  /** The forgot-password form was accepted; the API doesn't say whether the account exists. */
  resetLinkRequested: boolean;
  emailConfirmation: EmailConfirmationStatus;
  /** The profile's "resend confirmation" was accepted. */
  confirmationEmailSent: boolean;
}

export const initialAuthState: AuthState = {
  user: null,
  status: 'unknown',
  pending: false,
  loginError: null,
  formErrors: [],
  profileSaved: false,
  resetLinkRequested: false,
  emailConfirmation: 'idle',
  confirmationEmailSent: false,
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
      ForgotPasswordPageActions.opened,
      ResetPasswordPageActions.opened,
      ProfilePageActions.opened,
      (state): AuthState => ({
        ...state,
        loginError: null,
        formErrors: [],
        profileSaved: false,
        resetLinkRequested: false,
        confirmationEmailSent: false,
      }),
    ),
    on(
      LoginPageActions.loginSubmitted,
      RegisterPageActions.registerSubmitted,
      InvitationPageActions.passwordSubmitted,
      ForgotPasswordPageActions.emailSubmitted,
      ResetPasswordPageActions.passwordSubmitted,
      ProfilePageActions.displayNameSubmitted,
      ProfilePageActions.confirmationEmailRequested,
      (state): AuthState => ({ ...state, pending: true, loginError: null, formErrors: [], profileSaved: false }),
    ),
    on(AuthApiActions.resetLinkRequested, (state): AuthState => ({ ...state, pending: false, resetLinkRequested: true })),
    on(AuthApiActions.confirmationEmailSent, (state): AuthState => ({ ...state, pending: false, confirmationEmailSent: true })),
    on(ConfirmEmailPageActions.linkOpened, (state): AuthState => ({ ...state, pending: true, emailConfirmation: 'idle' })),
    on(
      AuthApiActions.emailConfirmed,
      (state, { userId }): AuthState => ({
        ...state,
        pending: false,
        emailConfirmation: 'confirmed',
        // The link may belong to someone other than the signed-in user.
        user: state.user?.id === userId ? { ...state.user, isEmailConfirmed: true } : state.user,
      }),
    ),
    on(
      AuthApiActions.emailConfirmationFailed,
      (state, { status }): AuthState => ({ ...state, pending: false, emailConfirmation: status }),
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
      AuthApiActions.resetLinkRequestFailed,
      AuthApiActions.passwordResetFailed,
      AuthApiActions.confirmationEmailFailed,
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
