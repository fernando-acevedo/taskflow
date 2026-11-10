import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { authFeature } from '../auth/store/auth.feature';
import { ThemeMenu } from './theme-menu';
import { ThemeService } from './theme.service';

describe('ThemeMenu', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [
        ThemeMenu,
        TranslocoTestingModule.forRoot({
          langs: { en: { theme: { label: 'Theme', current: 'Theme: {{ theme }}', System: 'System', Light: 'Light', Dark: 'Dark' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideMockStore({ selectors: [{ selector: authFeature.selectUser, value: null }] })],
    });
  });

  afterEach(() => localStorage.clear());

  async function open() {
    const fixture = TestBed.createComponent(ThemeMenu);
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')!;
    button.click();
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, button };
  }

  it('lists the three options with the current one checked, and has no axe violations', async () => {
    const { fixture, button } = await open();
    expect(button.getAttribute('aria-label')).toBe('Theme: System');
    const items = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitemradio"]'));
    expect(items.map((item) => item.querySelector('span')?.textContent?.trim())).toEqual(['System', 'Light', 'Dark']);
    expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['true', 'false', 'false']);
    await expectNoAxeViolations(fixture);
    await expectNoAxeViolations(document.querySelector<HTMLElement>('.mat-mdc-menu-panel')!);
  });

  it('choosing an option changes the theme', async () => {
    await open();
    document.querySelectorAll<HTMLElement>('[role="menuitemradio"]')[2].click();
    expect(TestBed.inject(ThemeService).preference()).toBe('Dark');
  });
});
