import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DemoService } from './demo.service';

describe('DemoService', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { service: TestBed.inject(DemoService), http: TestBed.inject(HttpTestingController) };
  }

  it('exposes the status returned by the API', () => {
    const { service, http } = setup();
    http.expectOne((r) => r.url.endsWith('/api/demo/status')).flush({ enabled: true, nextResetAt: null, accounts: [] });

    expect(service.status().enabled).toBe(true);
  });

  it('treats a failed call as a disabled demo', () => {
    const { service, http } = setup();
    http.expectOne((r) => r.url.endsWith('/api/demo/status')).flush(null, { status: 500, statusText: 'Server Error' });

    expect(service.status().enabled).toBe(false);
  });
});
