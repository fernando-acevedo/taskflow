import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { AdminUsersApiService } from '../admin-users-api.service';
import { AdminUsersApiActions, UsersPageActions } from './admin-users.actions';
import { adminUsersFeature } from './admin-users.feature';

export const loadUsers = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(AdminUsersApiService)) =>
    actions$.pipe(
      ofType(
        UsersPageActions.opened,
        UsersPageActions.queryChanged,
        AdminUsersApiActions.userCreated,
        AdminUsersApiActions.userUpdated,
        // A failure can mean the list is out of date (another admin changed the user), so it reloads too.
        AdminUsersApiActions.userUpdateFailed,
      ),
      withLatestFrom(store.select(adminUsersFeature.selectQuery)),
      switchMap(([, query]) =>
        api.list(query).pipe(
          map((page) => AdminUsersApiActions.usersLoaded({ page })),
          catchError((error: unknown) => of(AdminUsersApiActions.usersLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

// Writes run one after another with concatMap: each is about a different user, so none may be dropped.

export const createUser = createEffect(
  (actions$ = inject(Actions), api = inject(AdminUsersApiService)) =>
    actions$.pipe(
      ofType(UsersPageActions.createSubmitted),
      concatMap(({ user, submissionId }) =>
        api.create(user).pipe(
          map((created) => AdminUsersApiActions.userCreated({ invitationSent: created.invitationSent, submissionId })),
          catchError((error: unknown) =>
            of(AdminUsersApiActions.userUpdateFailed({ error: toApiError(error), submissionId })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const resendInvitation = createEffect(
  (actions$ = inject(Actions), api = inject(AdminUsersApiService)) =>
    actions$.pipe(
      ofType(UsersPageActions.invitationResendClicked),
      concatMap(({ id }) =>
        api.resendInvitation(id).pipe(
          map(() => AdminUsersApiActions.invitationSent()),
          catchError((error: unknown) => of(AdminUsersApiActions.userUpdateFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const updateUser = createEffect(
  (actions$ = inject(Actions), api = inject(AdminUsersApiService)) =>
    actions$.pipe(
      ofType(UsersPageActions.activeChanged, UsersPageActions.adminChanged),
      concatMap((action) =>
        (action.type === UsersPageActions.activeChanged.type
          ? api.setActive(action.id, action.active)
          : api.setAdmin(action.id, action.admin)
        ).pipe(
          map(() => AdminUsersApiActions.userUpdated()),
          catchError((error: unknown) => of(AdminUsersApiActions.userUpdateFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(
        AdminUsersApiActions.userCreated,
        AdminUsersApiActions.invitationSent,
        AdminUsersApiActions.userUpdated,
        AdminUsersApiActions.userUpdateFailed,
      ),
      tap((action) => {
        switch (action.type) {
          case AdminUsersApiActions.userCreated.type:
            if (action.invitationSent) {
              notifier.success('users.created');
            } else {
              notifier.error({ code: 'Users.InvitationNotSent', fields: {} });
            }
            break;
          case AdminUsersApiActions.invitationSent.type:
            notifier.success('users.invitationSent');
            break;
          case AdminUsersApiActions.userUpdated.type:
            notifier.success('users.saved');
            break;
          default:
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
