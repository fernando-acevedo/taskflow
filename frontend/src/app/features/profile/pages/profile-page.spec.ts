import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { provideMockStore } from '@ngrx/store/testing';
import { Component } from '@angular/core';
import { authFeature } from '@core/auth/store/auth.feature';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import { ApiKeysCard } from '@features/api-keys';
import { PREFERENCES_CARD_LOADER, ProfilePage } from './profile-page';

const loadCard = vi.fn();

@Component({ selector: 'app-api-keys-card', template: '' })
class ApiKeysStub {}

@Component({ selector: 'app-fake-card', template: '<p id="card">card</p>' })
class FakeCard {}

describe('ProfilePage preferences card', () => {
  beforeEach(() => {
    loadCard.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideMockStore({
          selectors: [
            { selector: authFeature.selectUser, value: null },
            { selector: authFeature.selectIsAdmin, value: false },
            { selector: tenantFeature.selectRole, value: null },
            { selector: authFeature.selectPending, value: false },
            { selector: authFeature.selectFormErrors, value: {} },
            { selector: authFeature.selectProfileSaved, value: false },
            { selector: authFeature.selectConfirmationEmailSent, value: false },
          ],
        }),
        { provide: PREFERENCES_CARD_LOADER, useValue: loadCard },
        provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
    });
  });

  it('keeps the page and shows a retry when the card chunk fails, then loads it on retry', async () => {
    loadCard.mockRejectedValueOnce(new Error('chunk')).mockResolvedValueOnce(FakeCard);
    TestBed.overrideComponent(ProfilePage, { remove: { imports: [ApiKeysCard] }, add: { imports: [ApiKeysStub] } });
    const component = TestBed.createComponent(ProfilePage).componentInstance as unknown as {
      preferencesFailed: () => boolean;
      preferencesCard: () => unknown;
      loadPreferences: () => void;
    };
    await new Promise((resolve) => setTimeout(resolve));

    expect(component.preferencesFailed()).toBe(true);
    expect(component.preferencesCard()).toBeNull();

    component.loadPreferences();
    expect(component.preferencesFailed()).toBe(false);
    await new Promise((resolve) => setTimeout(resolve));

    expect(component.preferencesCard()).toBe(FakeCard);
  });
});
