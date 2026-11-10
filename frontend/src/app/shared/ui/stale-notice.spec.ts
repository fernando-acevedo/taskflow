import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../../public/i18n/en.json';
import { expectNoAxeViolations } from '../testing/axe';
import { StaleNotice } from './stale-notice';

describe('StaleNotice', () => {
  function render() {
    TestBed.configureTestingModule({
      imports: [
        StaleNotice,
        TranslocoTestingModule.forRoot({
          langs: { en }, // the real file: a mis-nested key must fail here
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    });
    const fixture = TestBed.createComponent(StaleNotice);
    fixture.componentRef.setInput('message', 'Someone changed this.');
    fixture.detectChanges();
    return fixture;
  }

  it('is an alert with the message, focuses Reload and has no accessibility violations', async () => {
    const fixture = render();
    TestBed.tick();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Someone changed this.');
    expect(document.activeElement?.textContent?.trim()).toBe('Reload');
    await expectNoAxeViolations(fixture);
  });

  it('emits the user choice', () => {
    const fixture = render();
    const reload = vi.fn();
    const overwrite = vi.fn();
    fixture.componentInstance.reload.subscribe(reload);
    fixture.componentInstance.overwrite.subscribe(overwrite);
    const [reloadButton, overwriteButton] = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));

    reloadButton.click();
    overwriteButton.click();

    expect(reload).toHaveBeenCalledOnce();
    expect(overwrite).toHaveBeenCalledOnce();
  });
});
