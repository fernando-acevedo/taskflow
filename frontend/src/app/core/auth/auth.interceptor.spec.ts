import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { environment } from '../../../environments/environment';
import { authInterceptor } from './auth.interceptor';
import { AuthApiActions } from './store/auth.actions';
import { TokenStorage } from './token-storage';

const api = `${environment.apiUrl}/api`;
const unauthorized = { status: 401, statusText: 'Unauthorized' };

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokens: TokenStorage;
  let store: MockStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideMockStore({
          initialState: { tenant: { tenants: [], currentId: null, status: 'ready' } },
        }),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokens = TestBed.inject(TokenStorage);
    store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');
    tokens.save({ tokenType: 'Bearer', accessToken: 'old-access', expiresIn: 3600, refreshToken: 'refresh-1' });
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('adds the access token to API calls but not to the auth endpoints', () => {
    http.get(`${api}/users/me`).subscribe();
    http.post(`${api}/auth/login`, {}).subscribe();

    expect(httpMock.expectOne(`${api}/users/me`).request.headers.get('Authorization')).toBe('Bearer old-access');
    expect(httpMock.expectOne(`${api}/auth/login`).request.headers.has('Authorization')).toBe(false);
  });

  describe('tenant', () => {
    it('adds the current tenant to API calls', () => {
      store.setState({ tenant: { tenants: [], currentId: 'tenant-1', status: 'ready' } });
      http.get(`${api}/projects`).subscribe();

      expect(httpMock.expectOne(`${api}/projects`).request.headers.get('X-Tenant')).toBe(
        'tenant-1',
      );
    });

    it('adds nothing when no tenant is chosen', () => {
      http.get(`${api}/projects`).subscribe();

      expect(httpMock.expectOne(`${api}/projects`).request.headers.has('X-Tenant')).toBe(false);
    });

    it('leaves out the auth endpoints and the list of own tenants', () => {
      store.setState({ tenant: { tenants: [], currentId: 'tenant-1', status: 'ready' } });
      http.post(`${api}/auth/login`, {}).subscribe();
      http.get(`${api}/tenants/mine`).subscribe();

      expect(httpMock.expectOne(`${api}/auth/login`).request.headers.has('X-Tenant')).toBe(false);
      expect(httpMock.expectOne(`${api}/tenants/mine`).request.headers.has('X-Tenant')).toBe(false);
    });

    it('keeps the tenant on the retry after a refresh', () => {
      store.setState({ tenant: { tenants: [], currentId: 'tenant-1', status: 'ready' } });
      http.get(`${api}/projects`).subscribe();
      httpMock.expectOne(`${api}/projects`).flush(null, unauthorized);
      httpMock
        .expectOne(`${api}/auth/refresh`)
        .flush({
          tokenType: 'Bearer',
          accessToken: 'new-access',
          expiresIn: 3600,
          refreshToken: 'refresh-2',
        });

      const retry = httpMock.expectOne(`${api}/projects`);
      expect(retry.request.headers.get('X-Tenant')).toBe('tenant-1');
      expect(retry.request.headers.get('Authorization')).toBe('Bearer new-access');
      retry.flush([]);
    });
  });

  it('refreshes once for concurrent 401s and retries every request with the new token', () => {
    const responses: unknown[] = [];
    http.get(`${api}/users/me`).subscribe((body) => responses.push(body));
    http.get(`${api}/admin/users`).subscribe((body) => responses.push(body));

    httpMock.expectOne(`${api}/users/me`).flush(null, unauthorized);
    httpMock.expectOne(`${api}/admin/users`).flush(null, unauthorized);

    const refresh = httpMock.expectOne(`${api}/auth/refresh`);
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-1' });
    refresh.flush({ tokenType: 'Bearer', accessToken: 'new-access', expiresIn: 3600, refreshToken: 'refresh-2' });

    const retries = httpMock.match((request) => request.headers.get('Authorization') === 'Bearer new-access');
    expect(retries).toHaveLength(2);
    retries.forEach((retry) => retry.flush({ ok: true }));

    expect(responses).toHaveLength(2);
    expect(tokens.refreshToken()).toBe('refresh-2');
    expect(store.dispatch).not.toHaveBeenCalled();
  });

  it('ends the session when the refresh token is rejected', () => {
    let failed = false;
    http.get(`${api}/users/me`).subscribe({ error: () => (failed = true) });

    httpMock.expectOne(`${api}/users/me`).flush(null, unauthorized);
    httpMock.expectOne(`${api}/auth/refresh`).flush(null, unauthorized);

    expect(failed).toBe(true);
    expect(store.dispatch).toHaveBeenCalledWith(AuthApiActions.sessionExpired());
  });

  it.each([
    { status: 0, statusText: 'Unknown Error' },
    { status: 503, statusText: 'Service Unavailable' },
  ])('keeps the session when the refresh fails with $status', (refreshFailure) => {
    let failed = false;
    http.get(`${api}/users/me`).subscribe({ error: () => (failed = true) });

    httpMock.expectOne(`${api}/users/me`).flush(null, unauthorized);
    httpMock.expectOne(`${api}/auth/refresh`).flush(null, refreshFailure);

    expect(failed).toBe(true);
    expect(store.dispatch).not.toHaveBeenCalled();
    expect(tokens.refreshToken()).toBe('refresh-1');
  });

  it('passes other errors through without refreshing', () => {
    let status = 0;
    http.get(`${api}/users/me`).subscribe({ error: (error: HttpErrorResponse) => (status = error.status) });

    httpMock.expectOne(`${api}/users/me`).flush(null, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
  });
});
