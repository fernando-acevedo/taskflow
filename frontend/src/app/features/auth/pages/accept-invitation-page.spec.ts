import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { InvitationPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { AcceptInvitationPage } from './accept-invitation-page';

describe('AcceptInvitationPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([{ path: 'accept-invitation', component: AcceptInvitationPage }]),
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
    const harness = await RouterTestingHarness.create('/accept-invitation#email=ana%40example.com');

    expect(harness.routeNativeElement?.textContent).toContain('errors.invitationInvalid');
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
  });

  it('removes the link from the address bar but keeps it for the form', async () => {
    const harness = await RouterTestingHarness.create('/accept-invitation#email=ana%40example.com&token=CfDJ8abc');
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/accept-invitation');
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
    expect(harness.routeNativeElement?.querySelector<HTMLInputElement>('input[type=email]')?.value).toBe('ana@example.com');
  });

  it('submits the token and email exactly as the link encoded them', async () => {
    const harness = await RouterTestingHarness.create(
      '/accept-invitation#email=ana%2Btest%40example.com&token=CfDJ8a%2Bb%2Fc%3D%3D',
    );
    const store = TestBed.inject(MockStore);
    const dispatch = vi.spyOn(store, 'dispatch');
    const element = harness.routeNativeElement!;
    for (const input of element.querySelectorAll<HTMLInputElement>('input[type=password]')) {
      input.value = 'Secret123';
      input.dispatchEvent(new Event('input'));
    }

    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(dispatch).toHaveBeenCalledWith(
      InvitationPageActions.passwordSubmitted({
        acceptance: { email: 'ana+test@example.com', token: 'CfDJ8a+b/c==', password: 'Secret123' },
      }),
    );
  });
});
