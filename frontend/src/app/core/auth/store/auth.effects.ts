import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Observable, catchError, exhaustMap, map, of, switchMap, tap } from 'rxjs';
import { toApiError } from '../../http/api-error';
import { AuthApiService } from '../auth-api.service';
import { toFormErrors, toLoginError } from '../auth-errors';
import { Credentials, User } from '../auth.models';
import { safeReturnUrl } from '../return-url';
import { TokenStorage } from '../token-storage';
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

function signIn(api: AuthApiService, tokens: TokenStorage, credentials: Credentials): Observable<User> {
  return api.login(credentials).pipe(
    tap((response) => tokens.save(response)),
    switchMap(() => api.me()),
  );
}

export const login = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService), tokens = inject(TokenStorage)) =>
    actions$.pipe(
      ofType(LoginPageActions.loginSubmitted),
      exhaustMap(({ credentials, returnUrl }) =>
        signIn(api, tokens, credentials).pipe(
          map((user) => AuthApiActions.loginSucceeded({ user, returnUrl })),
          catchError((error: unknown) => of(AuthApiActions.loginFailed({ error: toLoginError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const register = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService), tokens = inject(TokenStorage)) =>
    actions$.pipe(
      ofType(RegisterPageActions.registerSubmitted),
      exhaustMap(({ credentials }) =>
        api.register(credentials).pipe(
          switchMap(() => signIn(api, tokens, credentials)),
          map((user) => AuthApiActions.loginSucceeded({ user, returnUrl: '/' })),
          catchError((error: unknown) => of(AuthApiActions.registerFailed({ errors: toFormErrors(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** Choosing the password signs the user in right away, as registering does. */
export const acceptInvitation = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService), tokens = inject(TokenStorage)) =>
    actions$.pipe(
      ofType(InvitationPageActions.passwordSubmitted),
      exhaustMap(({ acceptance }) =>
        api.acceptInvitation(acceptance).pipe(
          switchMap(() => signIn(api, tokens, { email: acceptance.email, password: acceptance.password })),
          map((user) => AuthApiActions.loginSucceeded({ user, returnUrl: '/' })),
          catchError((error: unknown) =>
            of(
              AuthApiActions.invitationAcceptFailed({
                errors: toApiError(error).code === 'Users.InvitationInvalid' ? ['invitationInvalid'] : toFormErrors(error),
              }),
            ),
          ),
        ),
      ),
    ),
  { functional: true },
);

/** Identity answers a wrong or expired confirmation code with 401; anything else (network, 5xx) is unexpected. */
export const confirmEmail = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService)) =>
    actions$.pipe(
      ofType(ConfirmEmailPageActions.linkOpened),
      exhaustMap(({ confirmation }) =>
        api.confirmEmail(confirmation).pipe(
          map(() => AuthApiActions.emailConfirmed({ userId: confirmation.userId })),
          catchError((error: unknown) =>
            of(
              AuthApiActions.emailConfirmationFailed({
                status: error instanceof HttpErrorResponse && error.status === 401 ? 'invalid' : 'unexpected',
              }),
            ),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const resendConfirmationEmail = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService)) =>
    actions$.pipe(
      ofType(ProfilePageActions.confirmationEmailRequested),
      exhaustMap(({ email }) =>
        api.resendConfirmationEmail(email).pipe(
          map(() => AuthApiActions.confirmationEmailSent()),
          catchError((error: unknown) => of(AuthApiActions.confirmationEmailFailed({ errors: toFormErrors(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const requestResetLink = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService)) =>
    actions$.pipe(
      ofType(ForgotPasswordPageActions.emailSubmitted),
      exhaustMap(({ email }) =>
        api.forgotPassword(email).pipe(
          map(() => AuthApiActions.resetLinkRequested()),
          catchError((error: unknown) => of(AuthApiActions.resetLinkRequestFailed({ errors: toFormErrors(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** Like accepting an invitation: the new password signs the user in right away. */
export const resetPassword = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService), tokens = inject(TokenStorage)) =>
    actions$.pipe(
      ofType(ResetPasswordPageActions.passwordSubmitted),
      exhaustMap(({ reset }) =>
        api.resetPassword(reset).pipe(
          switchMap(() => signIn(api, tokens, { email: reset.email, password: reset.newPassword })),
          map((user) => AuthApiActions.loginSucceeded({ user, returnUrl: '/' })),
          catchError((error: unknown) => {
            // Identity answers a wrong, used or expired code with the InvalidToken validation error.
            const errors = toFormErrors(error);
            return of(
              AuthApiActions.passwordResetFailed({ errors: errors.includes('InvalidToken') ? ['resetLinkInvalid'] : errors }),
            );
          }),
        ),
      ),
    ),
  { functional: true },
);

export const navigateAfterLogin = createEffect(
  (actions$ = inject(Actions), router = inject(Router)) =>
    actions$.pipe(
      ofType(AuthApiActions.loginSucceeded),
      tap(({ returnUrl }) => void router.navigateByUrl(safeReturnUrl(returnUrl))),
    ),
  { functional: true, dispatch: false },
);

/** Bearer tokens can't be revoked server-side one by one, so logging out means forgetting them. */
export const logout = createEffect(
  (actions$ = inject(Actions), router = inject(Router), tokens = inject(TokenStorage)) =>
    actions$.pipe(
      ofType(ShellActions.logoutClicked, AuthApiActions.sessionExpired),
      tap(() => {
        tokens.clear();
        void router.navigate(['/login']);
      }),
    ),
  { functional: true, dispatch: false },
);

export const updateProfile = createEffect(
  (actions$ = inject(Actions), api = inject(AuthApiService)) =>
    actions$.pipe(
      ofType(ProfilePageActions.displayNameSubmitted),
      exhaustMap(({ displayName }) =>
        api.updateProfile(displayName).pipe(
          map(() => AuthApiActions.profileUpdated({ displayName: displayName.trim() })),
          catchError((error: unknown) => of(AuthApiActions.profileUpdateFailed({ errors: toFormErrors(error) }))),
        ),
      ),
    ),
  { functional: true },
);
