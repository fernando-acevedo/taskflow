import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { provideMockStore } from '@ngrx/store/testing';
import { Subject, of, throwError } from 'rxjs';
import { SendChatMessageResult } from '@core/api/types';
import { Notifier } from '@core/notifications/notifier';
import { ChatbotApiService } from '../chatbot-api.service';
import { ChatApiActions, ChatWidgetActions } from './chatbot.actions';
import { chatbotFeature, initialChatbotState } from './chatbot.feature';
import { restartOnUnknownQuickReply, sendTurn } from './chatbot.effects';

function result(overrides: Partial<SendChatMessageResult['reply']> = {}, locale = 'es'): SendChatMessageResult {
  return {
    sessionId: 's1',
    locale,
    newSession: true,
    reply: {
      text: 'Hola',
      quickReplies: [{ id: 'a', label: 'A', url: null }],
      url: null,
      stepId: 'x',
      ended: false,
      outcome: null,
      isFallback: false,
      ...overrides,
    },
  };
}

describe('chatbot reducer', () => {
  const reduce = (state = initialChatbotState, action: Action) => chatbotFeature.reducer(state, action);

  it('starts a session with the requested locale and marks it as sending', () => {
    const state = reduce(undefined, ChatWidgetActions.sessionRequested({ locale: 'es' }));
    expect(state).toMatchObject({ locale: 'es', sending: true, messages: [] });
  });

  it('keeps the session, shows the bot message and clears answered quick replies', () => {
    let state = reduce(undefined, ChatApiActions.replyReceived({ result: result() }));
    expect(state.sessionId).toBe('s1');
    expect(state.messages[0].quickReplies).toHaveLength(1);

    state = reduce(state, ChatWidgetActions.quickReplySelected({ id: 'a', label: 'A' }));
    expect(state.sending).toBe(true);
    expect(state.messages.map((m) => [m.from, m.text, m.quickReplies.length])).toEqual([
      ['bot', 'Hola', 0],
      ['user', 'A', 0],
    ]);
  });

  it('records the outcome when the conversation ends', () => {
    const state = reduce(undefined, ChatApiActions.replyReceived({ result: result({ ended: true, outcome: 'Escalated' }) }));
    expect(state).toMatchObject({ ended: true, outcome: 'Escalated' });
    expect(state.messages[0].quickReplies).toEqual([]);
  });

  it('unlocks the input after a failure', () => {
    const sending = reduce(undefined, ChatWidgetActions.messageSent({ text: 'x' }));
    const state = reduce(sending, ChatApiActions.sendFailed({ error: { code: 'unexpected', fields: {} } }));
    expect(state.sending).toBe(false);
  });
});

describe('chatbot effects', () => {
  const send = vi.fn();
  let actions: Subject<Action>;

  function setup(state: { sessionId: string | null; locale: string | null }) {
    send.mockReset();
    actions = new Subject<Action>();
    TestBed.configureTestingModule({
      providers: [
        provideMockStore({ initialState: { chatbot: { ...initialChatbotState, ...state } } }),
        { provide: Actions, useValue: actions },
        { provide: ChatbotApiService, useValue: { send } },
        { provide: Notifier, useValue: { error: vi.fn() } },
      ],
    });
    return TestBed.runInInjectionContext(() => ({ turns: sendTurn(), restart: restartOnUnknownQuickReply() }));
  }

  it('opens a session with only the locale, then sends the quick reply id and the text with the session', () => {
    const { turns } = setup({ sessionId: 's1', locale: 'es' });
    send.mockReturnValue(of(result()));
    const emitted: Action[] = [];
    turns.subscribe((a) => emitted.push(a));

    actions.next(ChatWidgetActions.sessionRequested({ locale: 'en' }));
    actions.next(ChatWidgetActions.quickReplySelected({ id: 'q', label: 'Q' }));
    actions.next(ChatWidgetActions.messageSent({ text: 'hi' }));

    expect(send.mock.calls.map((c) => c[0] as unknown)).toEqual([
      { locale: 'en' },
      { sessionId: 's1', locale: 'es', quickReplyId: 'q' },
      { sessionId: 's1', locale: 'es', text: 'hi' },
    ]);
    expect(emitted).toHaveLength(3);
  });

  it('turns a failed call into sendFailed with the API code, and restarts on UnknownQuickReply', () => {
    const { turns, restart } = setup({ sessionId: 's1', locale: 'es' });
    send.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'Chatbot.UnknownQuickReply' } })));
    const emitted: Action[] = [];
    turns.subscribe((a) => emitted.push(a));
    const restarts: Action[] = [];
    restart.subscribe((a) => restarts.push(a));

    actions.next(ChatWidgetActions.quickReplySelected({ id: 'gone', label: 'x' }));
    expect(emitted[0]).toMatchObject({ type: ChatApiActions.sendFailed.type, error: { code: 'Chatbot.UnknownQuickReply' } });

    actions.next(emitted[0]);
    expect(restarts).toEqual([ChatWidgetActions.sessionRequested({ locale: 'es' })]);
  });
});
