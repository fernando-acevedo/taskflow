/** Public API of the auth feature: lazy loaders for its pages (the app routes use them). */
export const loadLoginPage = () => import('./pages/login-page').then((m) => m.LoginPage);
export const loadRegisterPage = () => import('./pages/register-page').then((m) => m.RegisterPage);
export const loadForgotPasswordPage = () => import('./pages/forgot-password-page').then((m) => m.ForgotPasswordPage);
export const loadConfirmEmailPage = () => import('./pages/confirm-email-page').then((m) => m.ConfirmEmailPage);
export const loadResetPasswordPage = () => import('./pages/reset-password-page').then((m) => m.ResetPasswordPage);
export const loadAcceptInvitationPage = () => import('./pages/accept-invitation-page').then((m) => m.AcceptInvitationPage);
