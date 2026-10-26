import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { ConfirmEmailPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { ConfirmEmailPage } from './confirm-email-page';

describe('ConfirmEmailPage', () => {
  let dispatch: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([{ path: 'confirm-email', component: ConfirmEmailPage }]),
        provideMockStore({
          selectors: [
            { selector: authFeature.selectPending, value: false },
            { selector: authFeature.selectEmailConfirmation, value: 'idle' },
            { selector: authFeature.selectIsAuthenticated, value: true },
          ],
        }),
      ],
    });
    dispatch = vi.spyOn(TestBed.inject(MockStore), 'dispatch');
  });

  it('confirms the email from the link, then removes the link from the address bar', async () => {
    const harness = await RouterTestingHarness.create('/confirm-email#userId=u1&code=Q2ZESjh-_x');
    await harness.fixture.whenStable();

    expect(dispatch).toHaveBeenCalledExactlyOnceWith(
      ConfirmEmailPageActions.linkOpened({ confirmation: { userId: 'u1', confirmationCode: 'Q2ZESjh-_x' } }),
    );
    expect(TestBed.inject(Router).url).toBe('/confirm-email');
  });

  it('explains that a link without its code is invalid, without calling the API', async () => {
    const harness = await RouterTestingHarness.create('/confirm-email#userId=u1');

    expect(harness.routeNativeElement?.textContent).toContain('errors.confirmationLinkInvalid');
    expect(dispatch).not.toHaveBeenCalled();
  });
});
