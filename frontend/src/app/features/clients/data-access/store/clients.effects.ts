import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { Observable, catchError, concatMap, filter, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { isStale, toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { ClientsApiService } from '../clients-api.service';
import { ClientsApiActions, ClientsPageActions } from './clients.actions';
import { clientsFeature } from './clients.feature';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';

export const loadClients = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ClientsApiService)) =>
    actions$.pipe(
      ofType(
        ClientsPageActions.opened,
        ClientsPageActions.queryChanged,
        ClientsApiActions.clientSaved,
        ClientsApiActions.clientSaveFailed,
      ),
      // A failed save reloads only when stale: the row versions in the store are behind the server's.
      filter((action) => action.type !== ClientsApiActions.clientSaveFailed.type || isStale(action.error)),
      withLatestFrom(store.select(clientsFeature.selectQuery)),
      switchMap(([, query]) =>
        api.list(query).pipe(
          untilTenantChanges(actions$),
          map((page) => ClientsApiActions.clientsLoaded({ page })),
          catchError((error: unknown) => of(ClientsApiActions.clientsLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const saveClient = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ClientsApiService)) =>
    actions$.pipe(
      ofType(
        ClientsPageActions.createSubmitted,
        ClientsPageActions.updateSubmitted,
        ClientsPageActions.archiveConfirmed,
        ClientsPageActions.restoreClicked,
      ),
      withLatestFrom(store.select(clientsFeature.selectEntities)),
      // One at a time, none dropped: archiving one client while another is saved must not get lost.
      concatMap(([action, entities]) => {
        let request: Observable<unknown>;
        let submissionId: number | undefined;
        if (action.type === ClientsPageActions.createSubmitted.type) {
          request = api.create(action.client);
          submissionId = action.submissionId;
        } else {
          // An edit carries the version its dialog opened (or last reloaded) with: a background reload of the
          // list must not make a save overwrite someone else's change. Archive and restore act on the shown row.
          const rowVersion =
            action.type === ClientsPageActions.updateSubmitted.type ? action.rowVersion : entities[action.id]?.rowVersion;
          if (entities[action.id] === undefined || rowVersion === undefined) {
            return of(
              ClientsApiActions.clientSaveFailed({
                error: { code: 'unexpected', fields: {} },
                submissionId: action.type === ClientsPageActions.updateSubmitted.type ? action.submissionId : undefined,
              }),
            );
          }
          if (action.type === ClientsPageActions.updateSubmitted.type) {
            request = api.update(action.id, action.client, rowVersion);
            submissionId = action.submissionId;
          } else {
            request = api.setArchived(action.id, action.type === ClientsPageActions.archiveConfirmed.type, rowVersion);
          }
        }
        return request.pipe(
          map(() => ClientsApiActions.clientSaved({ submissionId })),
          catchError((error: unknown) =>
            of(ClientsApiActions.clientSaveFailed({ error: toApiError(error), submissionId })),
          ),
        );
      }),
    ),
  { functional: true },
);

/** The edit dialog's "Reload": the server's clients, so the next save carries the current row version. */
export const refreshClients = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ClientsApiService)) =>
    actions$.pipe(
      ofType(ClientsPageActions.refreshRequested),
      withLatestFrom(store.select(clientsFeature.selectQuery)),
      concatMap(([{ submissionId }, query]) =>
        api.list(query).pipe(
          map((page) => ClientsApiActions.clientsRefreshed({ page, submissionId })),
          catchError((error: unknown) =>
            of(ClientsApiActions.clientSaveFailed({ error: toApiError(error), submissionId })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(ClientsApiActions.clientSaved, ClientsApiActions.clientSaveFailed),
      tap((action) => {
        if (action.type === ClientsApiActions.clientSaved.type) {
          notifier.success('clients.saved');
        } else if (!(isStale(action.error) && action.submissionId !== undefined)) {
          // A stale edit is shown inside the dialog that submitted it, not as a snackbar.
          notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
