import { HttpClient, HttpErrorResponse, HttpInterceptorFn, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { catchError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { sentTraceId } from '@shared/data-access/trace';
import { traceparentInterceptor } from './trace.interceptor';

const api = `${environment.apiUrl}/api`;
const format = /^00-([0-9a-f]{32})-([0-9a-f]{16})-01$/;

describe('traceparentInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([traceparentInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('adds a W3C traceparent to API calls, different for each request', () => {
    http.get(`${api}/a`).subscribe();
    http.get(`${api}/b`).subscribe();

    const a = httpMock.expectOne(`${api}/a`).request.headers.get('traceparent');
    const b = httpMock.expectOne(`${api}/b`).request.headers.get('traceparent');
    expect(a).toMatch(format);
    expect(b).toMatch(format);
    expect(a).not.toBe(b);
  });

  it('leaves other URLs alone', () => {
    http.get('https://example.com/x').subscribe();

    expect(httpMock.expectOne('https://example.com/x').request.headers.has('traceparent')).toBe(false);
  });

  it('keeps the trace id and changes the span id when the same request is sent again', () => {
    // Like authInterceptor's retry after a refresh: it sends the original request through the rest of the chain again.
    const retryOnce: HttpInterceptorFn = (request, next) => next(request).pipe(catchError(() => next(request)));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([retryOnce, traceparentInterceptor])), provideHttpClientTesting()],
    });
    const client = TestBed.inject(HttpClient);
    const mock = TestBed.inject(HttpTestingController);

    client.get(`${api}/a`).subscribe();
    const first = mock.expectOne(`${api}/a`);
    first.flush(null, { status: 401, statusText: 'Unauthorized' });
    const second = mock.expectOne(`${api}/a`);

    const one = format.exec(first.request.headers.get('traceparent') ?? '');
    const two = format.exec(second.request.headers.get('traceparent') ?? '');
    expect(one?.[1]).toBe(two?.[1]);
    expect(one?.[2]).not.toBe(two?.[2]);
    mock.verify();
  });

  it('remembers the trace id of a failed call', () => {
    let failure: unknown;
    http.get(`${api}/a`).subscribe({ error: (error: unknown) => (failure = error) });
    const call = httpMock.expectOne(`${api}/a`);
    const traceId = format.exec(call.request.headers.get('traceparent') ?? '')?.[1];
    call.flush(null, { status: 500, statusText: 'Server Error' });

    expect(sentTraceId(failure as HttpErrorResponse)).toBe(traceId);
  });
});
