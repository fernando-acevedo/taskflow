import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import en from '../../../../../public/i18n/en.json';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { Client } from '../data-access/clients-api.service';
import { ClientFormData, ClientFormDialog } from './client-form-dialog';
import { fakeDialogRef } from '@shared/testing/fake-dialog-ref';

const stale = { code: 'Concurrency.Stale', fields: {} };

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

describe('ClientFormDialog when someone else changed the client (stale)', () => {
  const close = vi.fn();

  function render(data: Partial<ClientFormData>) {
    close.mockClear();
    TestBed.configureTestingModule({
      imports: [
        ClientFormDialog,
        TranslocoTestingModule.forRoot({ langs: { en }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        { provide: MatDialogRef, useValue: fakeDialogRef(close) },
        { provide: MAT_DIALOG_DATA, useValue: { client: aClient(), save: vi.fn(), ...data } },
      ],
    });
    const fixture = TestBed.createComponent(ClientFormDialog);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;
  const nameInput = (fixture: ReturnType<typeof render>) =>
    root(fixture).querySelector<HTMLInputElement>('[formControlName="name"]')!;

  function typeName(fixture: ReturnType<typeof render>, value: string): void {
    nameInput(fixture).value = value;
    nameInput(fixture).dispatchEvent(new Event('input'));
  }

  function submit(fixture: ReturnType<typeof render>): void {
    root(fixture).querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function press(fixture: ReturnType<typeof render>, index: number): void {
    root(fixture).querySelectorAll<HTMLButtonElement>('app-stale-notice button')[index].click();
    fixture.detectChanges();
  }

  it('stays open with an alert and keeps what was typed', () => {
    const fixture = render({ save: vi.fn(() => of(stale)) });
    typeName(fixture, 'Mine');
    submit(fixture);

    expect(root(fixture).querySelector('[role="alert"]')).not.toBeNull();
    expect(nameInput(fixture).value).toBe('Mine');
    expect(close).not.toHaveBeenCalled();
  });

  it('Reload replaces the form with the server version', () => {
    const refresh = vi.fn(() => of(aClient({ name: 'Theirs', rowVersion: 'rv2' })));
    const fixture = render({ save: vi.fn(() => of(stale)), refresh });
    typeName(fixture, 'Mine');
    submit(fixture);

    press(fixture, 0);

    expect(nameInput(fixture).value).toBe('Theirs');
    expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
  });

  it('Save anyway fetches the server version and saves the typed values again', () => {
    const save = vi.fn().mockReturnValueOnce(of(stale)).mockReturnValueOnce(of(null));
    const refresh = vi.fn(() => of(aClient({ name: 'Theirs', rowVersion: 'rv2' })));
    const fixture = render({ save, refresh });
    typeName(fixture, 'Mine');
    submit(fixture);

    press(fixture, 1);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(save).toHaveBeenCalledTimes(2);
    expect(save).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Mine' }));
    expect(close).toHaveBeenCalledWith(true);
  });

  it('keeps the notice when the server version cannot be fetched', () => {
    const fixture = render({ save: vi.fn(() => of(stale)), refresh: vi.fn(() => of(null)) });
    typeName(fixture, 'Mine');
    submit(fixture);

    press(fixture, 0);

    expect(root(fixture).querySelector('[role="alert"]')).not.toBeNull();
    expect(nameInput(fixture).value).toBe('Mine');
  });

  it('focuses Reload when the stale notice appears and has no accessibility violations', async () => {
    const fixture = render({ save: vi.fn(() => of(stale)) });
    typeName(fixture, 'Mine');
    submit(fixture);
    TestBed.tick();

    expect(document.activeElement?.textContent?.trim()).toBe('Reload');
    await expectNoAxeViolations(fixture);
  });

  it('has no accessibility violations before any edit', async () => {
    await expectNoAxeViolations(render({}));
  });
});
