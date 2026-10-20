import { InvitationPageActions, LoginPageActions } from './store/auth.actions';
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

  it('masks the token in the router state', () => {
    const state = {
      router: {
        state: {
          url: '/accept-invitation?email=ana%40example.com&token=CfDJ8abc',
          root: { queryParams: { email: 'ana@example.com', token: 'CfDJ8abc' } },
        },
      },
    };

    expect(hideStateSecrets(state)).toEqual({
      router: {
        state: {
          url: '/accept-invitation?email=ana%40example.com&token=••••••',
          root: { queryParams: { email: 'ana@example.com', token: '••••••' } },
        },
      },
    });
  });
});
