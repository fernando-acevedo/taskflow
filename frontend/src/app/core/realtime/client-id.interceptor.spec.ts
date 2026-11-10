import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { CLIENT_ID_HEADER, clientId, resetClientIdForTests } from './client-id';
import { clientIdInterceptor } from './client-id.interceptor';

const api = `${environment.apiUrl}/api`;

describe('clientIdInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([clientIdInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends the same random tab id with every API call', () => {
    http.get(`${api}/a`).subscribe();
    http.post(`${api}/b`, {}).subscribe();

    const a = httpMock.expectOne(`${api}/a`).request.headers.get(CLIENT_ID_HEADER);
    const b = httpMock.expectOne(`${api}/b`).request.headers.get(CLIENT_ID_HEADER);
    expect(a).toBe(clientId());
    expect(b).toBe(a);
    expect(a).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('leaves other URLs alone', () => {
    http.get('https://example.com/x').subscribe();

    expect(httpMock.expectOne('https://example.com/x').request.headers.has(CLIENT_ID_HEADER)).toBe(false);
  });
});

describe('clientId without crypto.randomUUID', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    resetClientIdForTests();
  });

  it('builds a v4 UUID from getRandomValues', () => {
    resetClientIdForTests();
    const real = globalThis.crypto;
    vi.stubGlobal('crypto', { getRandomValues: (array: Uint8Array<ArrayBuffer>) => real.getRandomValues(array) });

    expect(clientId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });
});
