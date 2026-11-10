import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { ApiKeyFormDialog } from './api-key-form-dialog';
import { fakeDialogRef } from '@shared/testing/fake-dialog-ref';

describe('ApiKeyFormDialog', () => {
  function render(save = vi.fn(() => of(null))) {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [
        ApiKeyFormDialog,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        { provide: MatDialogRef, useValue: fakeDialogRef(close) },
        { provide: MAT_DIALOG_DATA, useValue: { save } },
      ],
    });
    const fixture = TestBed.createComponent(ApiKeyFormDialog);
    fixture.detectChanges();
    return { fixture, save, close };
  }

  function submit(fixture: ReturnType<typeof render>['fixture']): void {
    (fixture.nativeElement as HTMLElement).querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  it('does not save without a name', () => {
    const { fixture, save } = render();
    submit(fixture);

    expect(save).not.toHaveBeenCalled();
  });

  it('saves the name with the default scope and no expiry, then closes', () => {
    const { fixture, save, close } = render();
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[formcontrolname="name"]');
    if (!input) {
      throw new Error('name input not found');
    }
    input.value = '  CI  ';
    input.dispatchEvent(new Event('input'));
    submit(fixture);

    expect(save).toHaveBeenCalledWith({ name: 'CI', scope: 'Read', expiresAt: null });
    expect(close).toHaveBeenCalledWith(true);
  });

  it('has no accessibility violations', async () => {
    await expectNoAxeViolations(render().fixture);
  });
});
