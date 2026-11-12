import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SendChatMessageRequest, SendChatMessageResult } from '@core/api/types';
import { environment } from '../../../../environments/environment';

/** What a turn can carry; the API takes the rest as null. */
export interface ChatTurn {
  readonly sessionId?: string | null;
  readonly locale?: string | null;
  readonly text?: string | null;
  readonly quickReplyId?: string | null;
}

export const CHAT_MAX_LENGTH = 1000;

/** The support bot: anonymous, so it works on the sign-in pages too. */
@Injectable({ providedIn: 'root' })
export class ChatbotApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/api/chatbot/message`;

  send(turn: ChatTurn): Observable<SendChatMessageResult> {
    const body: SendChatMessageRequest = {
      sessionId: turn.sessionId ?? null,
      locale: turn.locale ?? null,
      text: turn.text ?? null,
      quickReplyId: turn.quickReplyId ?? null,
    };
    return this.http.post<SendChatMessageResult>(this.url, body);
  }
}
