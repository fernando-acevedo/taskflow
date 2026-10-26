import { InvitationPageActions, LoginPageActions, ResetPasswordPageActions } from './store/auth.actions';
import { hideSecrets, hideStateSecrets } from './devtools-sanitizer';

describe('devtools sanitizer', () => {
  it('masks the password of a login', () => {
    const action = LoginPageActions.loginSubmitted({
      credentials: { email: 'ana@example.com', password: 'Secret123' },
      returnUrl: '/',
    });

    expect(hideSecrets(action)).toEqual({
      ...action,
      credentials: { email: 'ana@example.com', password: '••••••' },
    });
  });

  it('masks the password and the token of an invitation acceptance', () => {
    const action = InvitationPageActions.passwordSubmitted({
      acceptance: { email: 'ana@example.com', token: 'CfDJ8abc', password: 'Secret123' },
    });

    expect(hideSecrets(action)).toEqual({
      ...action,
      acceptance: { email: 'ana@example.com', token: '••••••', password: '••••••' },
    });
  });

  it('masks the new password and the code of a password reset, also in the URL', () => {
    const action = ResetPasswordPageActions.passwordSubmitted({
      reset: { email: 'ana@example.com', resetCode: 'Q2ZESjh', newPassword: 'Secret123' },
    });
    const state = { router: { state: { url: '/reset-password#email=ana%40example.com&code=Q2ZESjh' } } };

    expect(hideSecrets(action)).toEqual({
      ...action,
      reset: { email: 'ana@example.com', resetCode: '••••••', newPassword: '••••••' },
    });
    expect(hideStateSecrets(state)).toEqual({
      router: { state: { url: '/reset-password#email=ana%40example.com&code=••••••' } },
    });
  });

  it('masks the token in the router state', () => {
    const state = {
      router: {
        state: {
          url: '/accept-invitation#email=ana%40example.com&token=CfDJ8abc',
          root: { fragment: 'token=CfDJ8abc&email=ana%40example.com', queryParams: { token: 'CfDJ8abc' } },
        },
      },
    };

    expect(hideStateSecrets(state)).toEqual({
      router: {
        state: {
          url: '/accept-invitation#email=ana%40example.com&token=••••••',
          root: { fragment: 'token=••••••&email=ana%40example.com', queryParams: { token: '••••••' } },
        },
      },
    });
  });
});
