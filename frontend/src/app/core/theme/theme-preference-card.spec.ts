import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { authFeature } from '../auth/store/auth.feature';
import { ThemePreferenceCard } from './theme-preference-card';
import { ThemeService } from './theme.service';

describe('ThemePreferenceCard', () => {
  afterEach(() => localStorage.clear());

  it('shows the three values, selects the current one and changes it', async () => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [
        ThemePreferenceCard,
        TranslocoTestingModule.forRoot({
          langs: { en: { theme: { title: 'Appearance', hint: 'Hint', System: 'System', Light: 'Light', Dark: 'Dark' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideMockStore({ selectors: [{ selector: authFeature.selectUser, value: null }] })],
    });
    const fixture = TestBed.createComponent(ThemePreferenceCard);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('mat-button-toggle button'));
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['System', 'Light', 'Dark']);
    expect(buttons[0].getAttribute('aria-checked') ?? buttons[0].getAttribute('aria-pressed')).toBe('true');
    buttons[1].click();
    fixture.detectChanges();
    expect(TestBed.inject(ThemeService).preference()).toBe('Light');
    await expectNoAxeViolations(fixture);
  });
});
