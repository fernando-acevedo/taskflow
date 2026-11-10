import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, concatMap, map, of, switchMap, tap } from 'rxjs';
import { toApiError } from '@shared/data-access/api-error';
import { Notifier } from '@core/notifications/notifier';
import { TenantMembersApiService } from '../tenant-members-api.service';
import { MembersApiActions, MembersPageActions } from './members.actions';
import { untilTenantChanges } from '@core/tenant/until-tenant-changes';

export const loadMembers = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(
        MembersPageActions.opened,
        MembersApiActions.memberAdded,
        MembersApiActions.memberUpdated,
        MembersApiActions.memberRemoved,
        // A failure can mean the list is out of date (another admin changed the member), so it reloads too.
        MembersApiActions.memberUpdateFailed,
      ),
      switchMap(() =>
        api.list().pipe(
          untilTenantChanges(actions$),
          map((members) => MembersApiActions.membersLoaded({ members })),
          catchError((error: unknown) =>
            of(MembersApiActions.membersLoadFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

/** The log is for owners; a failed load just leaves it empty. */
export const loadSupportAccess = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(MembersPageActions.supportAccessRequested),
      switchMap(() =>
        api.supportAccess().pipe(
          untilTenantChanges(actions$),
          map((items) => MembersApiActions.supportAccessLoaded({ items })),
          catchError(() => of(MembersApiActions.supportAccessLoaded({ items: [] }))),
        ),
      ),
    ),
  { functional: true },
);

// Writes run one after another with concatMap: each is about a different member, so none may be dropped.

export const addMember = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(MembersPageActions.addSubmitted),
      concatMap(({ member, submissionId }) =>
        api.add(member).pipe(
          map((added) =>
            MembersApiActions.memberAdded({ invitationSent: added.invitationSent, submissionId }),
          ),
          catchError((error: unknown) =>
            of(MembersApiActions.memberUpdateFailed({ error: toApiError(error), submissionId })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const resendInvitation = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(MembersPageActions.invitationResendClicked),
      concatMap(({ userId }) =>
        api.resendInvitation(userId).pipe(
          map(() => MembersApiActions.invitationSent()),
          catchError((error: unknown) =>
            of(MembersApiActions.memberUpdateFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const changeRole = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(MembersPageActions.roleChanged),
      concatMap(({ userId, role }) =>
        api.changeRole(userId, role).pipe(
          map(() => MembersApiActions.memberUpdated()),
          catchError((error: unknown) =>
            of(MembersApiActions.memberUpdateFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const removeMember = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(MembersPageActions.removeClicked),
      concatMap(({ userId }) =>
        api.remove(userId).pipe(
          map(() => MembersApiActions.memberRemoved()),
          catchError((error: unknown) =>
            of(MembersApiActions.memberUpdateFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const setActive = createEffect(
  (actions$ = inject(Actions), api = inject(TenantMembersApiService)) =>
    actions$.pipe(
      ofType(MembersPageActions.activeChanged),
      concatMap(({ userId, active }) =>
        api.setActive(userId, active).pipe(
          map(() => MembersApiActions.memberUpdated()),
          catchError((error: unknown) =>
            of(MembersApiActions.memberUpdateFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(
        MembersApiActions.memberAdded,
        MembersApiActions.invitationSent,
        MembersApiActions.memberUpdated,
        MembersApiActions.memberRemoved,
        MembersApiActions.memberUpdateFailed,
      ),
      tap((action) => {
        switch (action.type) {
          case MembersApiActions.memberAdded.type:
            if (action.invitationSent) {
              notifier.success('users.created');
            } else {
              notifier.error({ code: 'Users.InvitationNotSent', fields: {} });
            }
            break;
          case MembersApiActions.invitationSent.type:
            notifier.success('users.invitationSent');
            break;
          case MembersApiActions.memberUpdated.type:
            notifier.success('users.saved');
            break;
          case MembersApiActions.memberRemoved.type:
            notifier.success('users.removed');
            break;
          default:
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
