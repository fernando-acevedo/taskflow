import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { ResetPasswordPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { ResetPasswordPage } from './reset-password-page';

describe('ResetPasswordPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([{ path: 'reset-password', component: ResetPasswordPage }]),
        provideMockStore({
          selectors: [
            { selector: authFeature.selectPending, value: false },
            { selector: authFeature.selectFormErrors, value: [] },
          ],
        }),
      ],
    });
  });

  it('explains that a link without its code is invalid', async () => {
    const harness = await RouterTestingHarness.create('/reset-password#email=ana%40example.com');

    expect(harness.routeNativeElement?.textContent).toContain('errors.resetLinkInvalid');
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
  });

  it('removes the link from the address bar and submits the code with the new password', async () => {
    const harness = await RouterTestingHarness.create('/reset-password#email=ana%2Btest%40example.com&code=Q2ZESjh-_x');
    await harness.fixture.whenStable();
    const dispatch = vi.spyOn(TestBed.inject(MockStore), 'dispatch');
    const element = harness.routeNativeElement!;
    for (const input of element.querySelectorAll<HTMLInputElement>('input[type=password]')) {
      input.value = 'Secret123';
      input.dispatchEvent(new Event('input'));
    }

    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(TestBed.inject(Router).url).toBe('/reset-password');
    expect(dispatch).toHaveBeenCalledWith(
      ResetPasswordPageActions.passwordSubmitted({
        reset: { email: 'ana+test@example.com', resetCode: 'Q2ZESjh-_x', newPassword: 'Secret123' },
      }),
    );
  });
});
