import { DOCUMENT } from '@angular/common';
import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { EMPTY, catchError, concatMap, exhaustMap, filter, fromEvent, map, merge, of, switchMap, tap, timer } from 'rxjs';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { TenantActions, TenantApiActions } from '../../../../core/tenant/store/tenant.actions';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { NotificationsApiService, POLL_INTERVAL_MS } from '../notifications-api.service';
import {
  NotificationBellActions,
  NotificationPreferencesActions,
  NotificationsApiActions,
} from './notifications.actions';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';


/**
 * Asks for the unread count every 30 s from sign-in (or session restore) and again on a tenant switch, until sign-out or expiry, pausing
 * while the tab is hidden and asking again at once when it becomes visible (ADR 0010). A failed poll is
 * ignored: the next one tries again.
 */
export const pollUnreadCount = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService), document = inject(DOCUMENT)) =>
    actions$.pipe(
      ofType(
        TenantApiActions.tenantsLoaded,
        TenantActions.tenantSwitched,
        ShellActions.logoutClicked,
        AuthApiActions.sessionExpired,
      ),
      switchMap((action) =>
        action.type === TenantApiActions.tenantsLoaded.type ||
        action.type === TenantActions.tenantSwitched.type
          ? merge(
              timer(0, POLL_INTERVAL_MS),
              fromEvent(document, 'visibilitychange').pipe(filter(() => !document.hidden)),
            ).pipe(
              filter(() => !document.hidden),
              exhaustMap(() =>
                api.unreadCount().pipe(
                  map(({ count }) => NotificationsApiActions.unreadCountLoaded({ count })),
                  catchError(() => EMPTY),
                ),
              ),
            )
          : EMPTY,
      ),
    ),
  { functional: true },
);

/** Opening the bell reloads the list. */
export const loadNotifications = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService)) =>
    actions$.pipe(
      ofType(NotificationBellActions.opened),
      switchMap(() =>
        api.list().pipe(
          untilTenantChanges(actions$),
          map((page) => NotificationsApiActions.notificationsLoaded({ items: page.items })),
          catchError((error: unknown) => of(NotificationsApiActions.notificationsLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** ...and the count, which can be ahead of the last poll. */
export const refreshCountOnOpen = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService)) =>
    actions$.pipe(
      ofType(NotificationBellActions.opened),
      switchMap(() =>
        api.unreadCount().pipe(
          untilTenantChanges(actions$),
          map(({ count }) => NotificationsApiActions.unreadCountLoaded({ count })),
          catchError(() => EMPTY),
        ),
      ),
    ),
  { functional: true },
);

export const markRead = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService)) =>
    actions$.pipe(
      ofType(NotificationBellActions.notificationClicked),
      concatMap(({ id }) =>
        api.markRead(id).pipe(
          // Nothing to do on success: the reducer already showed it as read.
          concatMap(() => EMPTY),
          catchError((error: unknown) => of(NotificationsApiActions.markFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const markAllRead = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService)) =>
    actions$.pipe(
      ofType(NotificationBellActions.markAllClicked),
      concatMap(() =>
        api.markAllRead().pipe(
          concatMap(() => EMPTY),
          catchError((error: unknown) => of(NotificationsApiActions.markFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** A failed mark shows the message and puts the list and count back to what the server says. */
export const recoverFromMarkFailure = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(NotificationsApiActions.markFailed),
      tap(({ error }) => notifier.error(error)),
      map(() => NotificationBellActions.opened()),
    ),
  { functional: true },
);

export const loadPreferences = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService)) =>
    actions$.pipe(
      ofType(NotificationPreferencesActions.opened),
      switchMap(() =>
        api.preferences().pipe(
          untilTenantChanges(actions$),
          map((preferences) => NotificationsApiActions.preferencesLoaded({ preferences })),
          catchError((error: unknown) => of(NotificationsApiActions.preferencesLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const savePreferences = createEffect(
  (actions$ = inject(Actions), api = inject(NotificationsApiService)) =>
    actions$.pipe(
      ofType(NotificationPreferencesActions.saveSubmitted),
      concatMap(({ preferences }) =>
        api.updatePreferences(preferences).pipe(
          map((saved) => NotificationsApiActions.preferencesSaved({ preferences: saved })),
          catchError((error: unknown) => of(NotificationsApiActions.preferencesSaveFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const notifyPreferencesSaved = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(NotificationsApiActions.preferencesSaved, NotificationsApiActions.preferencesSaveFailed),
      tap((action) => {
        if ('error' in action) {
          notifier.error(action.error);
        } else {
          notifier.success('notifications.preferences.saved');
        }
      }),
    ),
  { functional: true, dispatch: false },
);
