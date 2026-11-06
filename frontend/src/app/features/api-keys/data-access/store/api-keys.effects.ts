import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { catchError, concatMap, map, of, switchMap, tap } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';
import { ApiKeysApiService } from '../api-keys-api.service';
import { ApiKeysApiActions, ApiKeysPageActions } from './api-keys.actions';

export const loadKeys = createEffect(
  (actions$ = inject(Actions), api = inject(ApiKeysApiService)) =>
    actions$.pipe(
      ofType(
        ApiKeysPageActions.opened,
        ApiKeysApiActions.keyCreated,
        ApiKeysApiActions.keyRevoked,
        // A failed write can mean the list is out of date.
        ApiKeysApiActions.writeFailed,
      ),
      switchMap(() =>
        api.list().pipe(
          untilTenantChanges(actions$),
          map((keys) => ApiKeysApiActions.keysLoaded({ keys })),
          catchError((error: unknown) =>
            of(ApiKeysApiActions.keysLoadFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const createKey = createEffect(
  (actions$ = inject(Actions), api = inject(ApiKeysApiService)) =>
    actions$.pipe(
      ofType(ApiKeysPageActions.createSubmitted),
      concatMap(({ key, submissionId }) =>
        api.create(key).pipe(
          map((created) =>
            ApiKeysApiActions.keyCreated({ secret: created.secret, name: created.name, submissionId }),
          ),
          catchError((error: unknown) =>
            of(ApiKeysApiActions.writeFailed({ error: toApiError(error), submissionId })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const revokeKey = createEffect(
  (actions$ = inject(Actions), api = inject(ApiKeysApiService)) =>
    actions$.pipe(
      ofType(ApiKeysPageActions.revokeConfirmed),
      concatMap(({ keyId }) =>
        api.revoke(keyId).pipe(
          map(() => ApiKeysApiActions.keyRevoked()),
          catchError((error: unknown) =>
            of(ApiKeysApiActions.writeFailed({ error: toApiError(error) })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(ApiKeysApiActions.keyRevoked, ApiKeysApiActions.writeFailed),
      tap((action) => {
        if (action.type === ApiKeysApiActions.keyRevoked.type) {
          notifier.success('profile.apiKeys.revoked');
        } else {
          notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
