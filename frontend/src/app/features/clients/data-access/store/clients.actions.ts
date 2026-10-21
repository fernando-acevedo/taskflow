import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError, PagedResult } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { Client, ClientInput, ClientQuery } from '../clients-api.service';

export const ClientsPageActions = createActionGroup({
  source: 'Clients Page',
  events: {
    Opened: emptyProps(),
    'Query Changed': props<{ query: Partial<ClientQuery> }>(),
    'Create Submitted': props<{ client: ClientInput } & Required<Submission>>(),
    'Update Submitted': props<{ id: string; client: ClientInput } & Required<Submission>>(),
    'Archive Confirmed': props<{ id: string }>(),
    'Restore Clicked': props<{ id: string }>(),
  },
});

export const ClientsApiActions = createActionGroup({
  source: 'Clients API',
  events: {
    'Clients Loaded': props<{ page: PagedResult<Client> }>(),
    'Clients Load Failed': props<{ error: ApiError }>(),
    'Client Saved': props<Submission>(),
    'Client Save Failed': props<{ error: ApiError } & Submission>(),
  },
});
