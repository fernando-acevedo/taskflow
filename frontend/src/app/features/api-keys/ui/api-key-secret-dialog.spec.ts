import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { ApiKeySecretDialog } from './api-key-secret-dialog';

describe('ApiKeySecretDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  function render() {
    TestBed.configureTestingModule({
      imports: [
        ApiKeySecretDialog,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [{ provide: MAT_DIALOG_DATA, useValue: { name: 'CI', secret: 'tf_ab12_SECRET' } }],
    });
    const fixture = TestBed.createComponent(ApiKeySecretDialog);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the secret with a warning, and copies it to the clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('code')?.textContent).toContain('tf_ab12_SECRET');
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
    element.querySelector<HTMLButtonElement>('.secret button')?.click();
    await fixture.whenStable();

    expect(writeText).toHaveBeenCalledWith('tf_ab12_SECRET');
  });

  it('tells the user when copying fails', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } });
    const fixture = render();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.secret button')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="status"]')?.textContent).toContain('copyFailed');
  });

  it('has no accessibility violations', async () => {
    await expectNoAxeViolations(render());
  });
});
