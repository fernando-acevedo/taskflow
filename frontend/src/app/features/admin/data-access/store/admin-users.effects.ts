import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, exhaustMap, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { AdminUsersApiService } from '../admin-users-api.service';
import { AdminUsersApiActions, UsersPageActions } from './admin-users.actions';
import { adminUsersFeature } from './admin-users.feature';

export const loadUsers = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(AdminUsersApiService)) =>
    actions$.pipe(
      ofType(UsersPageActions.opened, UsersPageActions.queryChanged, AdminUsersApiActions.userUpdated),
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

export const updateUser = createEffect(
  (actions$ = inject(Actions), api = inject(AdminUsersApiService)) =>
    actions$.pipe(
      ofType(UsersPageActions.activeChanged, UsersPageActions.adminChanged),
      exhaustMap((action) =>
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
      ofType(AdminUsersApiActions.userUpdated, AdminUsersApiActions.userUpdateFailed),
      tap((action) =>
        action.type === AdminUsersApiActions.userUpdated.type ? notifier.success('users.saved') : notifier.error(action.error),
      ),
    ),
  { functional: true, dispatch: false },
);
