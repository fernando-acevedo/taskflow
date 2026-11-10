import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideMockActions } from '@ngrx/effects/testing';
import { Action, Store, provideStore } from '@ngrx/store';
import { Subject } from 'rxjs';
import { LanguageService } from '@core/i18n/language.service';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { ApiKey } from '../data-access/api-keys-api.service';
import { ApiKeysApiActions, ApiKeysPageActions } from '../data-access/store/api-keys.actions';
import { apiKeysFeature } from '../data-access/store/api-keys.feature';
import { ApiKeySecretDialog } from './api-key-secret-dialog';
import { ApiKeysCard, apiKeyStatus } from './api-keys-card';

const base: ApiKey = {
  id: 'k1',
  name: 'CI',
  prefix: 'tf_ab12',
  scope: 'Read',
  createdAt: '2026-11-01T00:00:00Z',
  expiresAt: '2027-02-01T00:00:00Z',
  revokedAt: null,
  lastUsedAt: null,
  isActive: true,
};

describe('apiKeyStatus', () => {
  it('tells active, revoked and expired keys apart', () => {
    expect(apiKeyStatus(base)).toBe('active');
    expect(apiKeyStatus({ ...base, revokedAt: '2026-11-02T00:00:00Z', isActive: false })).toBe('revoked');
    expect(apiKeyStatus({ ...base, isActive: false })).toBe('expired');
  });
});

describe('ApiKeysCard', () => {
  let actions$: Subject<Action>;
  let open: ReturnType<typeof vi.fn>;

  function render(keys: ApiKey[]) {
    actions$ = new Subject<Action>();
    open = vi.fn();
    TestBed.configureTestingModule({
      imports: [
        ApiKeysCard,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideStore({ [apiKeysFeature.name]: apiKeysFeature.reducer }),
        provideMockActions(() => actions$),
        { provide: LanguageService, useValue: { current: signal('en') } },
        { provide: MatDialog, useValue: { open } },
      ],
    });
    const store = TestBed.inject(Store);
    store.dispatch(ApiKeysApiActions.keysLoaded({ keys }));
    const fixture = TestBed.createComponent(ApiKeysCard);
    fixture.detectChanges();
    return { fixture, store };
  }

  it('lists the keys with a revoke button only for the active ones', () => {
    const { fixture } = render([
      base,
      { ...base, id: 'k2', name: 'Old', revokedAt: '2026-11-02T00:00:00Z', isActive: false },
    ]);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('tf_ab12');
    expect(rows[0].querySelector('button')).not.toBeNull();
    expect(rows[1].querySelector('button')).toBeNull();
  });

  it('shows the secret once in a dialog when a key is created, and keeps it out of the store', () => {
    const { fixture, store } = render([]);
    const created = ApiKeysApiActions.keyCreated({ secret: 'tf_ab12_SECRET', name: 'CI', submissionId: 1 });

    actions$.next(created);
    store.dispatch(created);
    fixture.detectChanges();

    expect(open).toHaveBeenCalledWith(
      ApiKeySecretDialog,
      expect.objectContaining({ data: { name: 'CI', secret: 'tf_ab12_SECRET' } }),
    );
    let snapshot = '';
    store.subscribe((state) => (snapshot = JSON.stringify(state))).unsubscribe();
    expect(snapshot).not.toContain('SECRET');
  });

  it('asks for the list when it opens', () => {
    const { store } = render([]);
    const dispatch = vi.spyOn(store, 'dispatch');
    TestBed.createComponent(ApiKeysCard);

    expect(dispatch).toHaveBeenCalledWith(ApiKeysPageActions.opened());
  });

  it('has no accessibility violations', async () => {
    await expectNoAxeViolations(render([base]).fixture);
  });
});
