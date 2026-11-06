import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { environment } from '../../../environments/environment';
import { User } from '../auth/auth.models';
import { authFeature } from '../auth/store/auth.feature';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthApiActions } from '../auth/store/auth.actions';
import { Notifier } from '../notifications/notifier';
import { THEME_STORAGE_KEY } from './theme';
import { ThemeService } from './theme.service';


const user = { id: 'u1', email: 'a@b.c', displayName: 'Ada', roles: [] } as unknown as User;

describe('ThemeService', () => {
  let store: MockStore;
  const notifier = { error: vi.fn() };
  const url = `${environment.apiUrl}/api/users/me/preferences`;

  function create(saved: string | null): ThemeService {
    localStorage.clear();
    if (saved) {
      localStorage.setItem(THEME_STORAGE_KEY, saved);
    }
    TestBed.configureTestingModule({
      providers: [{ provide: Notifier, useValue: notifier }, provideHttpClient(), provideHttpClientTesting(), provideMockStore({ selectors: [{ selector: authFeature.selectUser, value: null }] })],
    });
    store = TestBed.inject(MockStore) as MockStore<object>;
    return TestBed.inject(ThemeService);
  }

  function signIn(value: User | null): void {
    store.overrideSelector(authFeature.selectUser, value);
    store.refreshState();
    TestBed.tick();
  }

  afterEach(() => {
    notifier.error.mockClear();
    localStorage.clear();
    document.documentElement.style.colorScheme = '';
  });

  it('starts from the saved value and applies it to <html>', () => {
    const service = create('Dark');
    TestBed.tick();
    expect(service.preference()).toBe('Dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });

  it('System leaves the color scheme to the browser', () => {
    const service = create(null);
    TestBed.tick();
    expect(service.preference()).toBe('System');
    expect(document.documentElement.style.colorScheme).toBe('light dark');
  });

  it('saves a change in the browser without calling the API when signed out', () => {
    const service = create(null);
    service.use('Light');
    TestBed.tick();
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('Light');
    expect(document.documentElement.style.colorScheme).toBe('light');
    TestBed.inject(HttpTestingController).verify();
  });

  it('the server value wins after login and is saved in the browser', () => {
    const service = create('Light');
    TestBed.tick();
    signIn({ ...user, themePreference: 'Dark' });
    expect(service.preference()).toBe('Dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('Dark');
  });

  it('keeps the last value on logout', () => {
    const service = create(null);
    TestBed.tick();
    signIn({ ...user, themePreference: 'Dark' });
    signIn(null);
    expect(service.preference()).toBe('Dark');
  });

  it('PUTs the preference when changed with a session', () => {
    const service = create(null);
    TestBed.tick();
    signIn(user);
    service.use('Dark');
    const request = TestBed.inject(HttpTestingController).expectOne(`${environment.apiUrl}/api/users/me/preferences`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ themePreference: 'Dark' });
    request.flush(null);
  });

  it('a re-emitted user with the old value does not undo a change', () => {
    const service = create(null);
    TestBed.tick();
    signIn({ ...user, themePreference: 'Light' });
    service.use('Dark');
    TestBed.inject(HttpTestingController).expectOne(url).flush(null);
    // A new object for the same user (e.g. a display name change) still carries the stale value.
    signIn({ ...user, displayName: 'Ada L.', themePreference: 'Light' });
    expect(service.preference()).toBe('Dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('Dark');
  });

  it('a change made signed out is replaced by the server value after login (ADR 0015)', () => {
    const service = create(null);
    TestBed.tick();
    service.use('Light');
    signIn({ ...user, themePreference: 'Dark' });
    expect(service.preference()).toBe('Dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('Dark');
  });

  it('a failed PUT warns and goes back to the last confirmed value', () => {
    const service = create(null);
    TestBed.tick();
    signIn({ ...user, themePreference: 'Light' });
    service.use('Dark');
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({ code: 'Users.NotFound' }, { status: 404, statusText: 'Not Found' } satisfies Partial<HttpErrorResponse>);
    TestBed.tick();
    expect(notifier.error).toHaveBeenCalledWith(expect.objectContaining({ code: 'Users.NotFound' }));
    expect(service.preference()).toBe('Light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('Light');
    expect(document.documentElement.style.colorScheme).toBe('light');
  });

  it('a confirmed PUT updates the stored user', () => {
    const service = create(null);
    TestBed.tick();
    signIn(user);
    const dispatch = vi.spyOn(store, 'dispatch');
    service.use('Dark');
    TestBed.inject(HttpTestingController).expectOne(url).flush(null);
    expect(dispatch).toHaveBeenCalledWith(AuthApiActions.themePreferenceSaved({ themePreference: 'Dark' }));
  });

  it('two quick changes are sent in order, one at a time', () => {
    const service = create(null);
    TestBed.tick();
    signIn(user);
    service.use('Dark');
    service.use('Light');
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(url);
    expect(first.request.body).toEqual({ themePreference: 'Dark' });
    http.expectNone(url);
    first.flush(null);
    const second = http.expectOne(url);
    expect(second.request.body).toEqual({ themePreference: 'Light' });
    second.flush(null);
    expect(service.preference()).toBe('Light');
  });
});
