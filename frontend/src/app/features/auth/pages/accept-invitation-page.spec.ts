import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { AcceptInvitationPage } from './accept-invitation-page';

describe('AcceptInvitationPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([{ path: 'accept-invitation', component: AcceptInvitationPage }], withComponentInputBinding()),
        provideMockStore({
          selectors: [
            { selector: authFeature.selectPending, value: false },
            { selector: authFeature.selectFormErrors, value: [] },
          ],
        }),
      ],
    });
  });

  it('explains that a link without its token is invalid', async () => {
    const harness = await RouterTestingHarness.create('/accept-invitation?email=ana%40example.com');

    expect(harness.routeNativeElement?.textContent).toContain('errors.invitationInvalid');
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
  });

  it('removes the token from the address bar but keeps it for the form', async () => {
    const harness = await RouterTestingHarness.create('/accept-invitation?email=ana%40example.com&token=CfDJ8abc');
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/accept-invitation?email=ana@example.com');
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
  });
});
