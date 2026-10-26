import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { of, throwError } from 'rxjs';
import { AuthApiService } from '../auth-api.service';
import { User } from '../auth.models';
import { TokenStorage } from '../token-storage';
import {
  AuthApiActions,
  ConfirmEmailPageActions,
  ForgotPasswordPageActions,
  InvitationPageActions,
  ProfilePageActions,
  ResetPasswordPageActions,
} from './auth.actions';
import { acceptInvitation, confirmEmail, requestResetLink, resendConfirmationEmail, resetPassword } from './auth.effects';

const acceptance = { email: 'ana@example.com', token: 'CfDJ8abc', password: 'Secret123' };
const user: User = {
  id: 'u1',
  email: 'ana@example.com',
  displayName: 'Ana',
  isActive: true,
  roles: [],
  isInvitationPending: false,
  isEmailConfirmed: true,
};
const tokens = { tokenType: 'Bearer', accessToken: 'access', expiresIn: 3600, refreshToken: 'refresh' };

function accept(api: Partial<AuthApiService>): { emitted: Action[]; storage: TokenStorage } {
  const storage = { save: vi.fn() } as unknown as TokenStorage;
  const emitted: Action[] = [];
  acceptInvitation(
    of(InvitationPageActions.passwordSubmitted({ acceptance })) as Actions,
    api as AuthApiService,
    storage,
  ).subscribe((action) => emitted.push(action));
  return { emitted, storage };
}

describe('acceptInvitation effect', () => {
  it('signs the user in with the password they chose', () => {
    const api = { acceptInvitation: vi.fn(() => of(undefined)), login: vi.fn(() => of(tokens)), me: vi.fn(() => of(user)) };

    const { emitted, storage } = accept(api);

    expect(api.login).toHaveBeenCalledWith({ email: acceptance.email, password: acceptance.password });
    expect(storage.save).toHaveBeenCalledWith(tokens);
    expect(emitted).toEqual([AuthApiActions.loginSucceeded({ user, returnUrl: '/' })]);
  });

  it('shows an invalid or expired link as such', () => {
    const rejected = new HttpErrorResponse({ status: 400, error: { code: 'Users.InvitationInvalid' } });

    const { emitted } = accept({ acceptInvitation: vi.fn(() => throwError(() => rejected)) });

    expect(emitted).toEqual([AuthApiActions.invitationAcceptFailed({ errors: ['invitationInvalid'] })]);
  });

  it('shows the password policy errors by Identity code', () => {
    const weak = new HttpErrorResponse({ status: 400, error: { errors: { PasswordTooShort: ['Too short'] } } });

    const { emitted } = accept({ acceptInvitation: vi.fn(() => throwError(() => weak)) });

    expect(emitted).toEqual([AuthApiActions.invitationAcceptFailed({ errors: ['PasswordTooShort'] })]);
  });
});

const reset = { email: 'ana@example.com', resetCode: 'Q2ZESjh', newPassword: 'Secret123' };

function submitReset(api: Partial<AuthApiService>): { emitted: Action[]; storage: TokenStorage } {
  const storage = { save: vi.fn() } as unknown as TokenStorage;
  const emitted: Action[] = [];
  resetPassword(
    of(ResetPasswordPageActions.passwordSubmitted({ reset })) as Actions,
    api as AuthApiService,
    storage,
  ).subscribe((action) => emitted.push(action));
  return { emitted, storage };
}

describe('resetPassword effect', () => {
  it('signs the user in with the new password', () => {
    const api = { resetPassword: vi.fn(() => of(undefined)), login: vi.fn(() => of(tokens)), me: vi.fn(() => of(user)) };

    const { emitted, storage } = submitReset(api);

    expect(api.login).toHaveBeenCalledWith({ email: reset.email, password: reset.newPassword });
    expect(storage.save).toHaveBeenCalledWith(tokens);
    expect(emitted).toEqual([AuthApiActions.loginSucceeded({ user, returnUrl: '/' })]);
  });

  it('shows a wrong, used or expired code as an invalid link', () => {
    const rejected = new HttpErrorResponse({ status: 400, error: { errors: { InvalidToken: ['Invalid token.'] } } });

    const { emitted } = submitReset({ resetPassword: vi.fn(() => throwError(() => rejected)) });

    expect(emitted).toEqual([AuthApiActions.passwordResetFailed({ errors: ['resetLinkInvalid'] })]);
  });

  it('shows the password policy errors by Identity code', () => {
    const weak = new HttpErrorResponse({ status: 400, error: { errors: { PasswordRequiresDigit: ['Needs a digit'] } } });

    const { emitted } = submitReset({ resetPassword: vi.fn(() => throwError(() => weak)) });

    expect(emitted).toEqual([AuthApiActions.passwordResetFailed({ errors: ['PasswordRequiresDigit'] })]);
  });
});

describe('requestResetLink effect', () => {
  function request(api: Partial<AuthApiService>): Action[] {
    const emitted: Action[] = [];
    requestResetLink(
      of(ForgotPasswordPageActions.emailSubmitted({ email: 'ana@example.com' })) as Actions,
      api as AuthApiService,
    ).subscribe((action) => emitted.push(action));
    return emitted;
  }

  it('reports the link as requested', () => {
    const api = { forgotPassword: vi.fn(() => of(undefined)) };

    expect(request(api)).toEqual([AuthApiActions.resetLinkRequested()]);
    expect(api.forgotPassword).toHaveBeenCalledWith('ana@example.com');
  });

  it('shows the rate limit as such', () => {
    const limited = new HttpErrorResponse({ status: 429 });

    expect(request({ forgotPassword: vi.fn(() => throwError(() => limited)) })).toEqual([
      AuthApiActions.resetLinkRequestFailed({ errors: ['tooManyAttempts'] }),
    ]);
  });
});

describe('confirmEmail effect', () => {
  const confirmation = { userId: 'u1', confirmationCode: 'Q2ZESjh' };

  function open(api: Partial<AuthApiService>): Action[] {
    const emitted: Action[] = [];
    confirmEmail(of(ConfirmEmailPageActions.linkOpened({ confirmation })) as Actions, api as AuthApiService).subscribe(
      (action) => emitted.push(action),
    );
    return emitted;
  }

  it('confirms the email', () => {
    const api = { confirmEmail: vi.fn(() => of(undefined)) };

    expect(open(api)).toEqual([AuthApiActions.emailConfirmed({ userId: 'u1' })]);
    expect(api.confirmEmail).toHaveBeenCalledWith(confirmation);
  });

  it('shows a rejected code as an invalid link', () => {
    const rejected = new HttpErrorResponse({ status: 401 });

    expect(open({ confirmEmail: vi.fn(() => throwError(() => rejected)) })).toEqual([
      AuthApiActions.emailConfirmationFailed({ status: 'invalid' }),
    ]);
  });

  it('keeps other failures apart from an invalid link', () => {
    const offline = new HttpErrorResponse({ status: 0 });

    expect(open({ confirmEmail: vi.fn(() => throwError(() => offline)) })).toEqual([
      AuthApiActions.emailConfirmationFailed({ status: 'unexpected' }),
    ]);
  });
});

describe('resendConfirmationEmail effect', () => {
  function request(api: Partial<AuthApiService>): Action[] {
    const emitted: Action[] = [];
    resendConfirmationEmail(
      of(ProfilePageActions.confirmationEmailRequested({ email: 'ana@example.com' })) as Actions,
      api as AuthApiService,
    ).subscribe((action) => emitted.push(action));
    return emitted;
  }

  it('reports the email as sent', () => {
    const api = { resendConfirmationEmail: vi.fn(() => of(undefined)) };

    expect(request(api)).toEqual([AuthApiActions.confirmationEmailSent()]);
    expect(api.resendConfirmationEmail).toHaveBeenCalledWith('ana@example.com');
  });

  it('shows the rate limit as such', () => {
    const limited = new HttpErrorResponse({ status: 429 });

    expect(request({ resendConfirmationEmail: vi.fn(() => throwError(() => limited)) })).toEqual([
      AuthApiActions.confirmationEmailFailed({ errors: ['tooManyAttempts'] }),
    ]);
  });
});
