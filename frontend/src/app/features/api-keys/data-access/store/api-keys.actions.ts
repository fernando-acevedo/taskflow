import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { ApiKey, NewApiKeyInput } from '../api-keys-api.service';

export const ApiKeysPageActions = createActionGroup({
  source: 'API Keys Page',
  events: {
    Opened: emptyProps(),
    'Create Submitted': props<{ key: NewApiKeyInput } & Required<Submission>>(),
    'Revoke Confirmed': props<{ keyId: string }>(),
  },
});

export const ApiKeysApiActions = createActionGroup({
  source: 'API Keys API',
  events: {
    'Keys Loaded': props<{ keys: ApiKey[] }>(),
    'Keys Load Failed': props<{ error: ApiError }>(),
    /**
     * The secret travels only in this action: no reducer keeps it, and the DevTools sanitizer masks it. The page
     * listens to the action stream to show it once.
     */
    'Key Created': props<{ secret: string; name: string } & Submission>(),
    'Key Revoked': emptyProps(),
    'Write Failed': props<{ error: ApiError } & Submission>(),
  },
});
