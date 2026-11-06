import { Action, Store } from '@ngrx/store';
import { Observable, Subject, of } from 'rxjs';
import { TenantActions } from '../../../../core/tenant/store/tenant.actions';
import { ClientsApiService } from '../clients-api.service';
import { ClientsApiActions, ClientsPageActions } from './clients.actions';
import { loadClients } from './clients.effects';
import { initialClientsState } from './clients.feature';

type ClientsResult = ReturnType<ClientsApiService['list']> extends Observable<infer T> ? T : never;

describe('clients effects', () => {
  function setup() {
    const actions$ = new Subject<Action>();
    const response$ = new Subject<ClientsResult>();
    const api = { list: vi.fn(() => response$) } as unknown as ClientsApiService;
    const store = { select: () => of(initialClientsState.query) } as unknown as Store;
    const emitted: Action[] = [];
    loadClients(actions$, store, api).subscribe((a) => emitted.push(a));
    return { actions$, response$, emitted };
  }

  const page = { items: [], totalCount: 0 } as unknown as ClientsResult;

  it('applies the answer of a load', () => {
    const { actions$, response$, emitted } = setup();

    actions$.next(ClientsPageActions.opened());
    response$.next(page);

    expect(emitted).toEqual([ClientsApiActions.clientsLoaded({ page })]);
  });

  it('drops the answer that arrives after the tenant was switched', () => {
    const { actions$, response$, emitted } = setup();

    actions$.next(ClientsPageActions.opened());
    actions$.next(TenantActions.tenantSwitched({ tenantId: 'other' }));
    response$.next(page);

    expect(emitted).toEqual([]);
  });
});
