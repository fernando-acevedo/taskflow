import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { Observable, catchError, exhaustMap, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { ClientsApiService } from '../clients-api.service';
import { ClientsApiActions, ClientsPageActions } from './clients.actions';
import { clientsFeature } from './clients.feature';

export const loadClients = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ClientsApiService)) =>
    actions$.pipe(
      ofType(ClientsPageActions.opened, ClientsPageActions.queryChanged, ClientsApiActions.clientSaved),
      withLatestFrom(store.select(clientsFeature.selectQuery)),
      switchMap(([, query]) =>
        api.list(query).pipe(
          map((page) => ClientsApiActions.clientsLoaded({ page })),
          catchError((error: unknown) => of(ClientsApiActions.clientsLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const saveClient = createEffect(
  (actions$ = inject(Actions), api = inject(ClientsApiService)) =>
    actions$.pipe(
      ofType(
        ClientsPageActions.createSubmitted,
        ClientsPageActions.updateSubmitted,
        ClientsPageActions.archiveConfirmed,
        ClientsPageActions.restoreClicked,
      ),
      exhaustMap((action) => {
        let request: Observable<unknown>;
        switch (action.type) {
          case ClientsPageActions.createSubmitted.type:
            request = api.create(action.client);
            break;
          case ClientsPageActions.updateSubmitted.type:
            request = api.update(action.id, action.client);
            break;
          default:
            request = api.setArchived(action.id, action.type === ClientsPageActions.archiveConfirmed.type);
        }
        return request.pipe(
          map(() => ClientsApiActions.clientSaved()),
          catchError((error: unknown) => of(ClientsApiActions.clientSaveFailed({ error: toApiError(error) }))),
        );
      }),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(ClientsApiActions.clientSaved, ClientsApiActions.clientSaveFailed),
      tap((action) =>
        action.type === ClientsApiActions.clientSaved.type ? notifier.success('clients.saved') : notifier.error(action.error),
      ),
    ),
  { functional: true, dispatch: false },
);
