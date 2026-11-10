import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { ClientsApiService } from './clients-api.service';

describe('ClientsApiService row versions', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    return { api: TestBed.inject(ClientsApiService), http: TestBed.inject(HttpTestingController) };
  }

  const url = `${environment.apiUrl}/api/clients`;

  it('sends the row version in the body of an update', () => {
    const { api, http } = setup();

    api.update('c1', { name: 'Acme', contactEmail: null, notes: null }, 'rv1').subscribe();

    const request = http.expectOne(`${url}/c1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'Acme', contactEmail: null, notes: null, rowVersion: 'rv1' });
    request.flush(null);
  });

  it('sends the row version when archiving and restoring', () => {
    const { api, http } = setup();

    api.setArchived('c1', true, 'rv1').subscribe();
    api.setArchived('c1', false, 'rv2').subscribe();

    const archive = http.expectOne(`${url}/c1/archive`);
    expect(archive.request.body).toEqual({ rowVersion: 'rv1' });
    archive.flush(null);
    const restore = http.expectOne(`${url}/c1/restore`);
    expect(restore.request.body).toEqual({ rowVersion: 'rv2' });
    restore.flush(null);
  });
});
