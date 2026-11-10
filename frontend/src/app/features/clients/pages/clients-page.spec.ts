import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Observable, of } from 'rxjs';
import { Notifier } from '@core/notifications/notifier';
import { Submissions } from '@shared/data-access/submission';
import { Client } from '../data-access/clients-api.service';
import { ClientsPageActions } from '../data-access/store/clients.actions';
import { clientsFeature, initialClientsState } from '../data-access/store/clients.feature';
import { ClientsPage } from './clients-page';

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

describe('ClientsPage', () => {
  const dialog = { open: vi.fn() };
  const notifier = { info: vi.fn() };
  const dispatchAndWait = vi.fn();
  let store: MockStore;
  let fixture: ComponentFixture<ClientsPage>;

  function render(options: { loading?: boolean; archived?: boolean } = {}): HTMLElement {
    dialog.open.mockReset();
    notifier.info.mockReset();
    dispatchAndWait.mockReset();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        ClientsPage,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: clientsFeature.selectAll, value: [aClient({ isArchived: options.archived ?? false })] },
            { selector: clientsFeature.selectQuery, value: initialClientsState.query },
            { selector: clientsFeature.selectTotalCount, value: 1 },
            { selector: clientsFeature.selectLoading, value: options.loading ?? false },
            { selector: clientsFeature.selectSaving, value: false },
            { selector: clientsFeature.selectLoadError, value: null },
          ],
        }),
        { provide: MatDialog, useValue: dialog },
        { provide: Notifier, useValue: notifier },
        { provide: Submissions, useValue: { dispatchAndWait } },
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    fixture = TestBed.createComponent(ClientsPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  interface EditData {
    save: (input: unknown) => Observable<unknown>;
    refresh: () => Observable<Client | null>;
  }

  function openEdit(): { data: EditData; ref: { close: ReturnType<typeof vi.fn> } } {
    const ref = { close: vi.fn() };
    dialog.open.mockReturnValue(ref);
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button[aria-label="clients.editNamed"]')?.click();
    if (dialog.open.mock.calls.length === 0) {
      (fixture.componentInstance as unknown as { openForm(client: Client): void }).openForm(aClient());
    }
    return { data: (dialog.open.mock.calls[0][1] as { data: EditData }).data, ref };
  }

  it('disables archive while the list reloads after a save', () => {
    const idle = render();
    expect(idle.querySelector<HTMLButtonElement>('button[aria-label="clients.archiveNamed"]')?.disabled).toBe(false);

    const busy = render({ loading: true });
    expect(busy.querySelector<HTMLButtonElement>('button[aria-label="clients.archiveNamed"]')?.disabled).toBe(true);
  });

  it('disables restore while the list reloads after a save', () => {
    expect(render({ loading: true, archived: true }).querySelector<HTMLButtonElement>('button[aria-label="clients.restoreNamed"]')?.disabled).toBe(true);
  });

  it('sends the version the dialog opened with, even if the list was reloaded meanwhile', () => {
    render();
    dispatchAndWait.mockReturnValue(of(null));
    const { data } = openEdit();
    store.overrideSelector(clientsFeature.selectAll, [aClient({ rowVersion: 'rv-theirs' })]);
    store.refreshState();
    const input = { name: 'Mine', contactEmail: null, notes: null };

    data.save(input);

    expect(dispatchAndWait).toHaveBeenCalledWith(expect.objectContaining({ type: ClientsPageActions.updateSubmitted.type, rowVersion: 'rv1' }));
  });

  it('takes the new version only after a successful refresh', () => {
    render();
    dispatchAndWait.mockReturnValue(of(null));
    const { data } = openEdit();
    store.overrideSelector(clientsFeature.selectAll, [aClient({ rowVersion: 'rv-theirs' })]);
    store.refreshState();

    data.refresh().subscribe();
    data.save({ name: 'Mine', contactEmail: null, notes: null });

    expect(dispatchAndWait).toHaveBeenLastCalledWith(expect.objectContaining({ rowVersion: 'rv-theirs' }));
  });

  it('says it no longer exists and closes the dialog when the client is gone after reloading', () => {
    render();
    dispatchAndWait.mockReturnValue(of(null));
    const { data, ref } = openEdit();
    store.overrideSelector(clientsFeature.selectAll, []);
    store.refreshState();
    const result = vi.fn();

    data.refresh().subscribe(result);

    expect(result).toHaveBeenCalledWith(null);
    expect(notifier.info).toHaveBeenCalledWith('common.gone');
    expect(ref.close).toHaveBeenCalled();
  });
});
