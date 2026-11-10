import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { Notifier } from '../../../../core/notifications/notifier';
import { Client, ClientsApiService } from '../clients-api.service';
import { ClientsApiActions, ClientsPageActions } from './clients.actions';
import { loadClients, notify, refreshClients, saveClient } from './clients.effects';
import { clientsFeature, initialClientsState } from './clients.feature';

function aClient(overrides: Partial<Client> = {}): Client {
  return {
    id: 'c1',
    name: 'Acme',
    contactEmail: null,
    notes: null,
    isArchived: false,
    activeProjectCount: 0,
    createdAt: '2026-11-01T10:00:00Z',
    rowVersion: 'rv1',
    ...overrides,
  };
}

function storeWith(clients: Client[]): Store {
  const entities = Object.fromEntries(clients.map((client) => [client.id, client]));
  return {
    select: (selector: unknown) => of(selector === clientsFeature.selectEntities ? entities : initialClientsState.query),
  } as unknown as Store;
}

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const input = { name: 'Acme 2', contactEmail: null, notes: null };
const stale = { code: 'Concurrency.Stale', fields: {} };

describe('clients: row versions and stale edits', () => {
  it('sends the stored row version with an update', () => {
    const api = { update: vi.fn(() => of(undefined)) } as unknown as ClientsApiService;
    const actions$ = of(ClientsPageActions.updateSubmitted({ id: 'c1', client: input, rowVersion: 'rv1', submissionId: 1 })) as Actions;

    const emitted = run(saveClient(actions$, storeWith([aClient()]), api));

    expect(api.update).toHaveBeenCalledWith('c1', input, 'rv1');
    expect(emitted).toEqual([ClientsApiActions.clientSaved({ submissionId: 1 })]);
  });

  it('sends the version the dialog opened with, not the one a background reload put in the store', () => {
    const api = { update: vi.fn(() => of(undefined)) } as unknown as ClientsApiService;
    const actions$ = of(ClientsPageActions.updateSubmitted({ id: 'c1', client: input, rowVersion: 'rv-opened', submissionId: 9 })) as Actions;

    run(saveClient(actions$, storeWith([aClient({ rowVersion: 'rv-theirs' })]), api));

    expect(api.update).toHaveBeenCalledWith('c1', input, 'rv-opened');
  });

  it('sends the stored row version when archiving and restoring', () => {
    const api = { setArchived: vi.fn(() => of(undefined)) } as unknown as ClientsApiService;
    const actions$ = of(ClientsPageActions.archiveConfirmed({ id: 'c1' }), ClientsPageActions.restoreClicked({ id: 'c1' })) as Actions;

    run(saveClient(actions$, storeWith([aClient()]), api));

    expect(api.setArchived).toHaveBeenNthCalledWith(1, 'c1', true, 'rv1');
    expect(api.setArchived).toHaveBeenNthCalledWith(2, 'c1', false, 'rv1');
  });

  it('does not call the API for a client the store does not hold', () => {
    const api = { update: vi.fn() } as unknown as ClientsApiService;
    const actions$ = of(ClientsPageActions.updateSubmitted({ id: 'zz', client: input, rowVersion: 'rv1', submissionId: 2 })) as Actions;

    const emitted = run(saveClient(actions$, storeWith([aClient()]), api));

    expect(api.update).not.toHaveBeenCalled();
    expect(emitted).toEqual([ClientsApiActions.clientSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 2 })]);
  });

  it('answers a stale update with the submission id so the dialog can show the notice', () => {
    const api = {
      update: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'Concurrency.Stale' } }))),
    } as unknown as ClientsApiService;
    const actions$ = of(ClientsPageActions.updateSubmitted({ id: 'c1', client: input, rowVersion: 'rv1', submissionId: 3 })) as Actions;

    const emitted = run(saveClient(actions$, storeWith([aClient()]), api));

    expect(emitted).toEqual([ClientsApiActions.clientSaveFailed({ error: stale, submissionId: 3 })]);
  });

  it('refreshes the list for the dialog, tagged with its submission id', () => {
    const page = { items: [aClient({ rowVersion: 'rv2' })], totalCount: 1, page: 1, pageSize: 25 };
    const api = { list: vi.fn(() => of(page)) } as unknown as ClientsApiService;
    const actions$ = of(ClientsPageActions.refreshRequested({ submissionId: 4 })) as Actions;

    const emitted = run(refreshClients(actions$, storeWith([]), api));

    expect(emitted).toEqual([ClientsApiActions.clientsRefreshed({ page, submissionId: 4 })]);
  });

  it('reloads the list after a stale failure, but not after other failures', () => {
    const page = { items: [], totalCount: 0, page: 1, pageSize: 25 };
    const api = { list: vi.fn(() => of(page)) } as unknown as ClientsApiService;
    const actions$ = of(
      ClientsApiActions.clientSaveFailed({ error: { code: 'unexpected', fields: {} } }),
      ClientsApiActions.clientSaveFailed({ error: stale }),
    ) as Actions;

    const emitted = run(loadClients(actions$, storeWith([]), api));

    expect(api.list).toHaveBeenCalledTimes(1);
    expect(emitted).toEqual([ClientsApiActions.clientsLoaded({ page })]);
  });

  it('keeps a stale edit out of the snackbar, but shows a stale archive and other errors', () => {
    const notifier = { success: vi.fn(), error: vi.fn() } as unknown as Notifier;
    const other = { code: 'unexpected', fields: {} };
    const actions$ = of(
      ClientsApiActions.clientSaveFailed({ error: stale, submissionId: 1 }),
      ClientsApiActions.clientSaveFailed({ error: stale }),
      ClientsApiActions.clientSaveFailed({ error: other, submissionId: 2 }),
    ) as Actions;

    run(notify(actions$, notifier));

    expect(notifier.error).toHaveBeenCalledTimes(2);
    expect(notifier.error).toHaveBeenNthCalledWith(1, stale);
    expect(notifier.error).toHaveBeenNthCalledWith(2, other);
  });

  it('takes the row versions of a refresh without touching the loading flag', () => {
    const { reducer } = clientsFeature;
    const loaded = reducer(
      initialClientsState,
      ClientsApiActions.clientsLoaded({ page: { items: [aClient()], totalCount: 1 } as never }),
    );

    const state = reducer(
      { ...loaded, loading: true },
      ClientsApiActions.clientsRefreshed({ page: { items: [aClient({ name: 'Theirs', rowVersion: 'rv9' })], totalCount: 1 } as never }),
    );

    expect(state.entities['c1']?.rowVersion).toBe('rv9');
    expect(state.entities['c1']?.name).toBe('Theirs');
    expect(state.loading).toBe(true);
  });
});
