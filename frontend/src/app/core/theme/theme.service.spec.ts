import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { environment } from '../../../environments/environment';
import { User } from '../auth/auth.models';
import { authFeature } from '../auth/store/auth.feature';
import { THEME_STORAGE_KEY } from './theme';
import { ThemeService } from './theme.service';

class FakeMedia extends EventTarget {
  matches = false;
  change(matches: boolean): void {
    this.matches = matches;
    this.dispatchEvent(Object.assign(new Event('change'), { matches }));
  }
}

const user = { id: 'u1', email: 'a@b.c', displayName: 'Ada', roles: [] } as unknown as User;

describe('ThemeService', () => {
  let media: FakeMedia;
  let store: MockStore;

  function create(saved: string | null): ThemeService {
    localStorage.clear();
    if (saved) {
      localStorage.setItem(THEME_STORAGE_KEY, saved);
    }
    media = new FakeMedia();
    vi.stubGlobal('matchMedia', () => media);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideMockStore({ selectors: [{ selector: authFeature.selectUser, value: null }] })],
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
    vi.unstubAllGlobals();
    localStorage.clear();
    document.documentElement.style.colorScheme = '';
  });

  it('starts from the saved value and applies it to <html>', () => {
    const service = create('Dark');
    TestBed.tick();
    expect(service.preference()).toBe('Dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });

  it('System follows the media query', () => {
    const service = create(null);
    TestBed.tick();
    expect(service.preference()).toBe('System');
    expect(document.documentElement.style.colorScheme).toBe('light dark');
    expect(service.effective()).toBe('light');
    media.change(true);
    expect(service.effective()).toBe('dark');
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
});
