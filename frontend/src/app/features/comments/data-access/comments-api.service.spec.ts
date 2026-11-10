import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { CommentsApiService } from './comments-api.service';

describe('CommentsApiService row versions', () => {
  it('sends the row version in the body of an edit', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(CommentsApiService);
    const http = TestBed.inject(HttpTestingController);

    api.update('p1', 't1', 'c1', { body: 'Hi', mentionedUserIds: null }, 'rv1').subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/projects/p1/tasks/t1/comments/c1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ body: 'Hi', mentionedUserIds: null, rowVersion: 'rv1' });
    request.flush({});
  });
});
