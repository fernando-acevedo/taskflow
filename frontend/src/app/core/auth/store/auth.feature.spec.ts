import { HttpErrorResponse } from '@angular/common/http';
import { toFormErrors, toLoginError } from '../auth-errors';
import { User } from '../auth.models';
import {
  AuthApiActions,
  InvitationPageActions,
  LoginPageActions,
  ProfilePageActions,
  ShellActions,
} from './auth.actions';
import { authFeature, initialAuthState } from './auth.feature';

const admin: User = { id: '1', email: 'ada@tests.local', displayName: 'Ada', isActive: true, roles: ['Admin'], isInvitationPending: false, isEmailConfirmed: true };
const credentials = { email: admin.email, password: 'Passw0rd!' };

describe('authFeature', () => {
  const { reducer, selectIsAuthenticated, selectIsAdmin } = authFeature;

  it('starts in an unknown session state until the startup check finishes', () => {
    expect(initialAuthState.status).toBe('unknown');
    expect(reducer(undefined, AuthApiActions.noSession()).status).toBe('anonymous');
  });

  it('marks the user as signed in after login and exposes the admin role', () => {
    const pending = reducer(initialAuthState, LoginPageActions.loginSubmitted({ credentials, returnUrl: '/' }));
    const state = reducer(pending, AuthApiActions.loginSucceeded({ user: admin, returnUrl: '/' }));

    expect(pending.pending).toBe(true);
    expect(state.pending).toBe(false);
    expect(selectIsAuthenticated.projector(state.status)).toBe(true);
    expect(selectIsAdmin.projector(state.user)).toBe(true);
  });

  it('keeps the login error until the page is opened again', () => {
    const failed = reducer(initialAuthState, AuthApiActions.loginFailed({ error: 'lockedOut' }));

    expect(failed.loginError).toBe('lockedOut');
    expect(reducer(failed, LoginPageActions.opened()).loginError).toBeNull();
  });

  it('forgets the user on logout and on session expiry', () => {
    const signedIn = reducer(initialAuthState, AuthApiActions.sessionRestored({ user: admin }));

    for (const action of [ShellActions.logoutClicked(), AuthApiActions.sessionExpired()]) {
      const state = reducer(signedIn, action);
      expect(state.user).toBeNull();
      expect(state.status).toBe('anonymous');
    }
  });

  it('shows invitation errors until the page is opened again', () => {
    const acceptance = { email: admin.email, token: 'token', password: 'Passw0rd!' };
    const pending = reducer(initialAuthState, InvitationPageActions.passwordSubmitted({ acceptance }));
    const failed = reducer(pending, AuthApiActions.invitationAcceptFailed({ errors: ['invitationInvalid'] }));

    expect(pending.pending).toBe(true);
    expect(failed.pending).toBe(false);
    expect(failed.formErrors).toEqual(['invitationInvalid']);
    expect(reducer(failed, InvitationPageActions.opened()).formErrors).toEqual([]);
  });

  it('updates the display name after a successful save', () => {
    const signedIn = reducer(initialAuthState, AuthApiActions.sessionRestored({ user: admin }));
    const saving = reducer(signedIn, ProfilePageActions.displayNameSubmitted({ displayName: 'Ada L.' }));
    const saved = reducer(saving, AuthApiActions.profileUpdated({ displayName: 'Ada L.' }));

    expect(saved.user?.displayName).toBe('Ada L.');
    expect(saved.profileSaved).toBe(true);
  });

  it('marks the signed-in user as confirmed only when the link was theirs', () => {
    const signedIn = reducer(
      initialAuthState,
      AuthApiActions.sessionRestored({ user: { ...admin, isEmailConfirmed: false } }),
    );

    const someoneElse = reducer(signedIn, AuthApiActions.emailConfirmed({ userId: 'other' }));
    const theirs = reducer(signedIn, AuthApiActions.emailConfirmed({ userId: admin.id }));

    expect(someoneElse.user?.isEmailConfirmed).toBe(false);
    expect(someoneElse.emailConfirmation).toBe('confirmed');
    expect(theirs.user?.isEmailConfirmed).toBe(true);
  });

  it('shows the resent confirmation until the profile is opened again', () => {
    const sent = reducer(initialAuthState, AuthApiActions.confirmationEmailSent());

    expect(sent.confirmationEmailSent).toBe(true);
    expect(reducer(sent, ProfilePageActions.opened()).confirmationEmailSent).toBe(false);
  });
});

describe('auth error mapping', () => {
  const httpError = (status: number, error: unknown = null) => new HttpErrorResponse({ status, error });

  it('maps login failures to messages the page can translate', () => {
    expect(toLoginError(httpError(401, { detail: 'Failed' }))).toBe('invalidCredentials');
    expect(toLoginError(httpError(401, { detail: 'LockedOut' }))).toBe('lockedOut');
    expect(toLoginError(httpError(429))).toBe('tooManyAttempts');
    expect(toLoginError(httpError(0))).toBe('unexpected');
  });

  it('turns a validation problem into its error codes', () => {
    const problem = { errors: { DuplicateUserName: ['…'], PasswordTooShort: ['…'] } };

    expect(toFormErrors(httpError(400, problem))).toEqual(['DuplicateUserName', 'PasswordTooShort']);
    expect(toFormErrors(httpError(500))).toEqual(['unexpected']);
  });
});
