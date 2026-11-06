import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { DemoService } from '../../../core/demo/demo.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { expectNoAxeViolations } from '../../../shared/testing/axe';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  function render(loginError: string | null) {
    TestBed.configureTestingModule({
      imports: [
        LoginPage,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([]),
        provideMockStore({
          selectors: [
            { selector: authFeature.selectPending, value: false },
            { selector: authFeature.selectLoginError, value: loginError },
          ],
        }),
        {
          provide: DemoService,
          useValue: {
            status: signal({
              enabled: true,
              nextResetAt: '2026-11-07T03:00:00Z',
              accounts: [{ role: 'Admin', email: 'admin@demo.local', password: 'Demo12345' }],
            }),
          },
        },
        { provide: LanguageService, useValue: { current: signal('en') } },
      ],
    });
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    return fixture;
  }

  it('has no accessibility violations, with the demo panel and with an error', async () => {
    await expectNoAxeViolations(render(null));
    TestBed.resetTestingModule();
    await expectNoAxeViolations(render('invalidCredentials'));
  });
});
