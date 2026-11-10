import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { App } from './app';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import { authFeature } from '@core/auth/store/auth.feature';
import { DemoService, DemoStatus } from '@core/demo/demo.service';
import { LanguageService } from '@core/i18n/language.service';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { notificationsFeature } from '@features/notifications';

const demoOn: DemoStatus = { enabled: true, nextResetAt: null, accounts: [] };
const tenants = [
  { id: 't1', name: 'Acme', slug: 'acme', isDemo: false, role: 'Owner' as const },
  { id: 't2', name: 'Globex', slug: 'globex', isDemo: false, role: 'Member' as const },
];
const user = { id: 'u1', email: 'a@b.c', displayName: 'Ada', roles: [] };

function render(status: DemoStatus, signedIn: boolean): HTMLElement {
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } })],
    providers: [
      provideRouter([]),
      provideMockStore({
        selectors: [
          { selector: authFeature.selectUser, value: signedIn ? user : null },
          { selector: authFeature.selectIsAdmin, value: false },
          { selector: tenantFeature.selectCanManage, value: false },
          { selector: tenantFeature.selectTenants, value: signedIn ? tenants : [] },
          { selector: tenantFeature.selectCurrent, value: signedIn ? tenants[0] : null },
          { selector: notificationsFeature.selectUnreadCount, value: 0 },
          { selector: notificationsFeature.selectItems, value: [] },
          { selector: notificationsFeature.selectLoading, value: false },
          { selector: notificationsFeature.selectLoadError, value: null },
        ],
      }),
      { provide: DemoService, useValue: { status: signal(status) } },
      { provide: LanguageService, useValue: { current: signal('en'), use: () => undefined } },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('App demo indicator', () => {
  it('shows while signed in on a demo', () => {
    expect(render(demoOn, true).querySelector('.demo-badge')).not.toBeNull();
  });

  it('is hidden when the demo is disabled', () => {
    expect(render({ ...demoOn, enabled: false }, true).querySelector('.demo-badge')).toBeNull();
  });

  it('is hidden when signed out', () => {
    expect(render(demoOn, false).querySelector('.demo-badge')).toBeNull();
  });

  it('has no accessibility violations, signed in or out', async () => {
    await expectNoAxeViolations(render(demoOn, true));
    TestBed.resetTestingModule();
    await expectNoAxeViolations(render(demoOn, false));
  });
});

describe('App shell', () => {
  it('moves focus to the main region from the skip link', () => {
    const root = render(demoOn, true);
    const main = root.querySelector<HTMLElement>('#main')!;
    root.querySelector<HTMLElement>('.skip-link')!.click();

    expect(document.activeElement).toBe(main);
  });

  it('names each language in its own language for assistive technology', () => {
    const root = render(demoOn, true);
    const labels = [...root.querySelectorAll('mat-button-toggle button')].map((b) => b.getAttribute('aria-label'));

    expect(labels).toEqual(['English', 'Espa\u00f1ol']);
  });
});
