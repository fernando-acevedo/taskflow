import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { LabelsApiService } from './labels-api.service';

describe('LabelsApiService row versions', () => {
  it('sends the row version in the body of an update and returns the saved label', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(LabelsApiService);
    const http = TestBed.inject(HttpTestingController);
    const saved = { id: 'l1', name: 'Bug', color: '#D32F2F', rowVersion: 'rv2' };
    let result: unknown;

    api.update('p1', 'l1', { name: 'Bug', color: '#D32F2F' }, 'rv1').subscribe((label) => (result = label));

    const request = http.expectOne(`${environment.apiUrl}/api/projects/p1/labels/l1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'Bug', color: '#D32F2F', rowVersion: 'rv1' });
    request.flush(saved);
    expect(result).toEqual(saved);
  });
});
