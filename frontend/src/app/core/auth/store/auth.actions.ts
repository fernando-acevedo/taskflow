import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { Credentials, LoginErrorCode, User } from '../auth.models';

export const LoginPageActions = createActionGroup({
  source: 'Login Page',
  events: {
    Opened: emptyProps(),
    'Login Submitted': props<{ credentials: Credentials; returnUrl: string }>(),
  },
});

export const RegisterPageActions = createActionGroup({
  source: 'Register Page',
  events: {
    Opened: emptyProps(),
    'Register Submitted': props<{ credentials: Credentials }>(),
  },
});

export const ProfilePageActions = createActionGroup({
  source: 'Profile Page',
  events: {
    Opened: emptyProps(),
    'Display Name Submitted': props<{ displayName: string }>(),
  },
});

export const ShellActions = createActionGroup({
  source: 'Shell',
  events: {
    'Logout Clicked': emptyProps(),
  },
});

export const AuthApiActions = createActionGroup({
  source: 'Auth API',
  events: {
    'Session Restored': props<{ user: User }>(),
    'No Session': emptyProps(),
    'Login Succeeded': props<{ user: User; returnUrl: string }>(),
    'Login Failed': props<{ error: LoginErrorCode }>(),
    'Register Failed': props<{ errors: string[] }>(),
    'Profile Updated': props<{ displayName: string }>(),
    'Profile Update Failed': props<{ errors: string[] }>(),
    'Session Expired': emptyProps(),
  },
});
