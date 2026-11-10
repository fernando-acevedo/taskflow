import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import en from '../../../../../public/i18n/en.json';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { Label } from '../data-access/labels-api.service';
import { LabelFormData, LabelFormDialog } from './label-form-dialog';
import { fakeDialogRef } from '@shared/testing/fake-dialog-ref';

const stale = { code: 'Concurrency.Stale', fields: {} };
const mine: Label = { id: 'l1', name: 'Bug', color: '#D32F2F', rowVersion: 'rv1' };

describe('LabelFormDialog when someone else changed the label (stale)', () => {
  const close = vi.fn();

  function render(data: Partial<LabelFormData>) {
    close.mockClear();
    TestBed.configureTestingModule({
      imports: [
        LabelFormDialog,
        TranslocoTestingModule.forRoot({ langs: { en }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        { provide: MatDialogRef, useValue: fakeDialogRef(close) },
        { provide: MAT_DIALOG_DATA, useValue: { label: mine, save: vi.fn(), ...data } },
      ],
    });
    const fixture = TestBed.createComponent(LabelFormDialog);
    fixture.detectChanges();
    return fixture;
  }

  const root = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;
  const nameInput = (fixture: ReturnType<typeof render>) => root(fixture).querySelector<HTMLInputElement>('input[matInput]')!;

  function typeAndSubmit(fixture: ReturnType<typeof render>, value: string): void {
    nameInput(fixture).value = value;
    nameInput(fixture).dispatchEvent(new Event('input'));
    root(fixture).querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  function press(fixture: ReturnType<typeof render>, index: number): void {
    root(fixture).querySelectorAll<HTMLButtonElement>('app-stale-notice button')[index].click();
    fixture.detectChanges();
  }

  it('stays open with an alert and keeps what was typed', () => {
    const fixture = render({ save: vi.fn(() => of(stale)) });

    typeAndSubmit(fixture, 'Mine');

    expect(root(fixture).querySelector('[role="alert"]')).not.toBeNull();
    expect(nameInput(fixture).value).toBe('Mine');
    expect(close).not.toHaveBeenCalled();
  });

  it('Reload replaces the form with the server version', () => {
    const refresh = vi.fn(() => of({ ...mine, name: 'Theirs', rowVersion: 'rv2' }));
    const fixture = render({ save: vi.fn(() => of(stale)), refresh });
    typeAndSubmit(fixture, 'Mine');

    press(fixture, 0);

    expect(nameInput(fixture).value).toBe('Theirs');
    expect(root(fixture).querySelector('[role="alert"]')).toBeNull();
  });

  it('Save anyway fetches the server version and saves the typed values again', () => {
    const save = vi.fn().mockReturnValueOnce(of(stale)).mockReturnValueOnce(of(null));
    const refresh = vi.fn(() => of({ ...mine, rowVersion: 'rv2' }));
    const fixture = render({ save, refresh });
    typeAndSubmit(fixture, 'Mine');

    press(fixture, 1);

    expect(save).toHaveBeenCalledTimes(2);
    expect(save).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Mine' }));
    expect(close).toHaveBeenCalledWith(true);
  });

  it('focuses Reload when the stale notice appears and has no accessibility violations', async () => {
    const fixture = render({ save: vi.fn(() => of(stale)) });
    typeAndSubmit(fixture, 'Mine');
    TestBed.tick();

    expect(document.activeElement?.textContent?.trim()).toBe('Reload');
    await expectNoAxeViolations(fixture);
  });

  it('has no accessibility violations before any edit', async () => {
    await expectNoAxeViolations(render({}));
  });
});
