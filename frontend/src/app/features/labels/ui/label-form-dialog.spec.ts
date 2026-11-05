import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Subject, of } from 'rxjs';
import { ApiError } from '../../../core/http/api-error';
import { LabelFormData, LabelFormDialog } from './label-form-dialog';

describe('LabelFormDialog', () => {
  const close = vi.fn();

  function render(label: LabelFormData['label'], save: LabelFormData['save'] = () => of(null)) {
    close.mockClear();
    TestBed.configureTestingModule({
      imports: [
        LabelFormDialog,
        TranslocoTestingModule.forRoot({
          langs: { en: { labels: { nameRule: 'Name rule' }, apiErrors: { Labels: { NameTaken: 'Name taken' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { label, save } satisfies LabelFormData },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(LabelFormDialog);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const nameInput = element.querySelector<HTMLInputElement>('input[matInput]')!;
    return {
      fixture,
      element,
      name: nameInput,
      radios: () => [...element.querySelectorAll<HTMLInputElement>('input[type=radio]')],
      submit: () => {
        element.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
        fixture.detectChanges();
      },
      type: (value: string) => {
        nameInput.value = value;
        nameInput.dispatchEvent(new Event('input'));
        fixture.detectChanges();
      },
    };
  }

  it('creates a label with the typed name and the default color', () => {
    const save = vi.fn(() => of(null));
    const view = render(null, save);

    view.type('  Bug ');
    view.submit();

    expect(save).toHaveBeenCalledWith({ name: 'Bug', color: '#0288D1' });
    expect(close).toHaveBeenCalledWith(true);
  });

  it('starts from the label being edited', () => {
    const save = vi.fn(() => of(null));
    const view = render({ id: 'l1', name: 'Bug', color: '#D32F2F' }, save);

    expect(view.name.value).toBe('Bug');
    expect(view.radios().find((radio) => radio.checked)?.value).toBe('#D32F2F');
    view.submit();
    expect(save).toHaveBeenCalledWith({ name: 'Bug', color: '#D32F2F' });
  });

  it('rejects a blank name without saving', () => {
    const save = vi.fn(() => of(null));
    const view = render(null, save);

    view.type('   ');
    view.submit();

    expect(save).not.toHaveBeenCalled();
    expect(view.element.querySelector('mat-error')?.textContent).toContain('Name rule');
  });

  it('exposes the palette as one native radio group with readable names', () => {
    const radios = render(null).radios();

    expect(radios).toHaveLength(10);
    expect(new Set(radios.map((radio) => radio.name)).size).toBe(1);
    expect(radios[0]?.getAttribute('aria-label')).toBe('labels.colors.red');
  });

  it('selects a color when its radio changes (what arrow keys do on native radios)', () => {
    const save = vi.fn(() => of(null));
    const view = render(null, save);

    const target = view.radios()[0];
    target.checked = true;
    target.dispatchEvent(new Event('change'));
    view.fixture.detectChanges();
    view.type('Bug');
    view.submit();

    expect(save).toHaveBeenCalledWith({ name: 'Bug', color: '#D32F2F' });
  });

  it('shows NameTaken under the name field and keeps the dialog open', () => {
    const outcome = new Subject<ApiError | null>();
    const view = render(null, () => outcome);

    view.type('Bug');
    view.submit();
    outcome.next({ code: 'Labels.NameTaken', fields: {} });
    view.fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(view.element.querySelector('mat-error')?.textContent).toContain('Name taken');
  });
});
