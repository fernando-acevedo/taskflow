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
  InvitationPageActions,
  LoginPageActions,
  ProfilePageActions,
  RegisterPageActions,
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
