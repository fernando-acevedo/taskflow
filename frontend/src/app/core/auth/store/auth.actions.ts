import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { Credentials, EmailConfirmation, InvitationAcceptance, LoginErrorCode, PasswordReset, User } from '../auth.models';

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

export const InvitationPageActions = createActionGroup({
  source: 'Invitation Page',
  events: {
    Opened: emptyProps(),
    'Password Submitted': props<{ acceptance: InvitationAcceptance }>(),
  },
});

export const ForgotPasswordPageActions = createActionGroup({
  source: 'Forgot Password Page',
  events: {
    Opened: emptyProps(),
    'Email Submitted': props<{ email: string }>(),
  },
});

export const ResetPasswordPageActions = createActionGroup({
  source: 'Reset Password Page',
  events: {
    Opened: emptyProps(),
    'Password Submitted': props<{ reset: PasswordReset }>(),
  },
});

export const ConfirmEmailPageActions = createActionGroup({
  source: 'Confirm Email Page',
  events: {
    'Link Opened': props<{ confirmation: EmailConfirmation }>(),
  },
});

export const ProfilePageActions = createActionGroup({
  source: 'Profile Page',
  events: {
    Opened: emptyProps(),
    'Display Name Submitted': props<{ displayName: string }>(),
    'Confirmation Email Requested': props<{ email: string }>(),
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
    'Invitation Accept Failed': props<{ errors: string[] }>(),
    'Reset Link Requested': emptyProps(),
    'Reset Link Request Failed': props<{ errors: string[] }>(),
    'Password Reset Failed': props<{ errors: string[] }>(),
    'Email Confirmed': props<{ userId: string }>(),
    'Confirmation Email Sent': emptyProps(),
    'Confirmation Email Failed': props<{ errors: string[] }>(),
    'Email Confirmation Failed': props<{ status: 'invalid' | 'unexpected' }>(),
    'Profile Updated': props<{ displayName: string }>(),
    'Profile Update Failed': props<{ errors: string[] }>(),
    'Session Expired': emptyProps(),
  },
});
