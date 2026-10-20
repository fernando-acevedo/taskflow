import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError, PagedResult } from '../../../../core/http/api-error';
import { Client, ClientInput, ClientQuery } from '../clients-api.service';

export const ClientsPageActions = createActionGroup({
  source: 'Clients Page',
  events: {
    Opened: emptyProps(),
    'Query Changed': props<{ query: Partial<ClientQuery> }>(),
    'Create Submitted': props<{ client: ClientInput }>(),
    'Update Submitted': props<{ id: string; client: ClientInput }>(),
    'Archive Confirmed': props<{ id: string }>(),
    'Restore Clicked': props<{ id: string }>(),
  },
});

export const ClientsApiActions = createActionGroup({
  source: 'Clients API',
  events: {
    'Clients Loaded': props<{ page: PagedResult<Client> }>(),
    'Clients Load Failed': props<{ error: ApiError }>(),
    'Client Saved': emptyProps(),
    'Client Save Failed': props<{ error: ApiError }>(),
  },
});
