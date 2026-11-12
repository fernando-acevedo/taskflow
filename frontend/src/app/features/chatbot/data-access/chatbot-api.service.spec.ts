import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { ChatbotApiService } from './chatbot-api.service';

describe('ChatbotApiService', () => {
  it('posts the turn to the anonymous endpoint, with the fields it does not use as null', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(ChatbotApiService).send({ locale: 'es' }).subscribe();

    const request = http.expectOne(`${environment.apiUrl}/api/chatbot/message`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ sessionId: null, locale: 'es', text: null, quickReplyId: null });
    http.verify();
  });
});
