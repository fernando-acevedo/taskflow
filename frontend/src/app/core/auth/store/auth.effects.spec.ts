import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { of, throwError } from 'rxjs';
import { AuthApiService } from '../auth-api.service';
import { User } from '../auth.models';
import { TokenStorage } from '../token-storage';
import { AuthApiActions, InvitationPageActions } from './auth.actions';
import { acceptInvitation } from './auth.effects';

const acceptance = { email: 'ana@example.com', token: 'CfDJ8abc', password: 'Secret123' };
const user: User = {
  id: 'u1',
  email: 'ana@example.com',
  displayName: 'Ana',
  isActive: true,
  roles: [],
  isInvitationPending: false,
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
