import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Observable } from 'rxjs';
import { OnlineStatus } from '../network/online-status';
import { environment } from '../../../environments/environment';
import { BASE_DELAY_MS, MAX_ATTEMPTS, backoffDelay, retryInterceptor } from './retry.interceptor';
import { traceparentInterceptor } from './trace.interceptor';

const api = `${environment.apiUrl}/api`;

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(Math, 'random').mockReturnValue(1); // no shortening: the full backoff
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([retryInterceptor, traceparentInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function track(request: Observable<unknown>) {
    const result: { value?: unknown; error?: HttpErrorResponse } = {};
    request.subscribe({
      next: (value) => (result.value = value),
      error: (error: HttpErrorResponse) => (result.error = error),
    });
    return result;
  }

  it.each([0, 502, 503, 504])('sends a GET again after a %i, with a growing wait', async (status) => {
    const result = track(http.get(`${api}/projects`));

    httpMock.expectOne(`${api}/projects`).flush(null, { status, statusText: 'x' });
    httpMock.expectNone(`${api}/projects`); // waiting for the backoff
    await vi.advanceTimersByTimeAsync(BASE_DELAY_MS - 1);
    httpMock.expectNone(`${api}/projects`);
    await vi.advanceTimersByTimeAsync(1);

    httpMock.expectOne(`${api}/projects`).flush(null, { status, statusText: 'x' });
    await vi.advanceTimersByTimeAsync(BASE_DELAY_MS * 2 - 1);
    httpMock.expectNone(`${api}/projects`);
    await vi.advanceTimersByTimeAsync(1);

    httpMock.expectOne(`${api}/projects`).flush({ ok: true });
    expect(result.value).toEqual({ ok: true });
  });

  it('gives up after the last try and fails with the final error', async () => {
    const result = track(http.get(`${api}/projects`));

    for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
      httpMock.expectOne(`${api}/projects`).flush(null, { status: 503, statusText: 'Unavailable' });
      await vi.advanceTimersByTimeAsync(BASE_DELAY_MS * 2 ** attempt);
    }

    httpMock.expectNone(`${api}/projects`);
    expect(result.error?.status).toBe(503);
  });

  it.each([400, 401, 403, 404, 409, 500])('does not repeat a %i', async (status) => {
    const result = track(http.get(`${api}/projects`));

    httpMock.expectOne(`${api}/projects`).flush(null, { status, statusText: 'x' });
    await vi.advanceTimersByTimeAsync(60_000);

    httpMock.expectNone(`${api}/projects`);
    expect(result.error?.status).toBe(status);
  });

  it.each(['POST', 'PUT', 'PATCH', 'DELETE'])('never repeats a %s, even on a 503', async (method) => {
    const result = track(http.request(method, `${api}/projects`, { body: {} }));

    httpMock.expectOne(`${api}/projects`).flush(null, { status: 503, statusText: 'x' });
    await vi.advanceTimersByTimeAsync(60_000);

    httpMock.expectNone(`${api}/projects`);
    expect(result.error?.status).toBe(503);
  });

  it('leaves other URLs alone', async () => {
    const result = track(http.get('https://example.com/x'));

    httpMock.expectOne('https://example.com/x').flush(null, { status: 503, statusText: 'x' });
    await vi.advanceTimersByTimeAsync(60_000);

    httpMock.expectNone('https://example.com/x');
    expect(result.error?.status).toBe(503);
  });

  it('keeps the trace id of every try and gives each a new span id', async () => {
    vi.restoreAllMocks(); // the span ids come from Math.random; the longest first wait is BASE_DELAY_MS
    track(http.get(`${api}/projects`));

    const first = httpMock.expectOne(`${api}/projects`);
    const firstTrace = first.request.headers.get('traceparent') ?? '';
    first.flush(null, { status: 502, statusText: 'x' });
    await vi.advanceTimersByTimeAsync(BASE_DELAY_MS);

    const second = httpMock.expectOne(`${api}/projects`);
    const secondTrace = second.request.headers.get('traceparent') ?? '';
    second.flush({});

    const [, firstId, firstSpan] = firstTrace.split('-');
    const [, secondId, secondSpan] = secondTrace.split('-');
    expect(secondId).toBe(firstId);
    expect(secondSpan).not.toBe(firstSpan);
  });
});

describe('backoffDelay', () => {
  it('doubles the ceiling each retry and shortens it by at most half', () => {
    expect(backoffDelay(1, () => 1)).toBe(500);
    expect(backoffDelay(2, () => 1)).toBe(1000);
    expect(backoffDelay(3, () => 1)).toBe(2000);
    expect(backoffDelay(2, () => 0)).toBe(500);
  });
});

describe('retryInterceptor while offline', () => {
  it('does not retry a failed GET when the browser reports it is offline', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([retryInterceptor])),
        provideHttpClientTesting(),
        { provide: OnlineStatus, useValue: { online: signal(false).asReadonly() } },
      ],
    });
    const errors: HttpErrorResponse[] = [];
    TestBed.inject(HttpClient)
      .get(`${api}/projects`)
      .subscribe({ error: (error: HttpErrorResponse) => errors.push(error) });
    const httpMock = TestBed.inject(HttpTestingController);

    httpMock.expectOne(`${api}/projects`).flush(null, { status: 0, statusText: 'x' });

    expect(errors).toHaveLength(1);
    httpMock.verify();
  });
});
