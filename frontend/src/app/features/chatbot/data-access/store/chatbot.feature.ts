import { createFeature, createReducer, on } from '@ngrx/store';
import { ChatOutcome, ChatQuickReplyDto } from '@core/api/types';
import { ChatApiActions, ChatWidgetActions } from './chatbot.actions';

export interface ChatMessage {
  readonly id: number;
  readonly from: 'bot' | 'user';
  /** Plain text: rendered with interpolation, never as HTML (the bot's `{support}` may be an email). */
  readonly text: string;
  readonly quickReplies: readonly ChatQuickReplyDto[];
  readonly url: string | null;
  readonly isFallback: boolean;
}

export interface ChatbotState {
  /** Only in memory: a reload starts a new conversation. */
  sessionId: string | null;
  /** The language the server fixed for this session. */
  locale: string | null;
  messages: readonly ChatMessage[];
  sending: boolean;
  ended: boolean;
  outcome: ChatOutcome | null;
  nextId: number;
}

export const initialChatbotState: ChatbotState = {
  sessionId: null,
  locale: null,
  messages: [],
  sending: false,
  ended: false,
  outcome: null,
  nextId: 1,
};

function withUserMessage(state: ChatbotState, text: string): ChatbotState {
  return {
    ...state,
    sending: true,
    nextId: state.nextId + 1,
    messages: [
      // Answered quick replies go away: only the last bot message offers choices.
      ...state.messages.map((m) => (m.quickReplies.length > 0 ? { ...m, quickReplies: [] } : m)),
      { id: state.nextId, from: 'user', text, quickReplies: [], url: null, isFallback: false },
    ],
  };
}

export const chatbotFeature = createFeature({
  name: 'chatbot',
  reducer: createReducer(
    initialChatbotState,
    on(
      ChatWidgetActions.sessionRequested,
      (state, { locale }): ChatbotState => ({ ...initialChatbotState, locale, sending: true, nextId: state.nextId }),
    ),
    on(ChatWidgetActions.messageSent, (state, { text }): ChatbotState => withUserMessage(state, text)),
    on(ChatWidgetActions.quickReplySelected, (state, { label }): ChatbotState => withUserMessage(state, label)),
    on(ChatApiActions.replyReceived, (state, { result }): ChatbotState => ({
      ...state,
      sessionId: result.sessionId,
      locale: result.locale,
      sending: false,
      ended: result.reply.ended,
      outcome: result.reply.outcome,
      nextId: state.nextId + 1,
      messages: [
        ...state.messages,
        {
          id: state.nextId,
          from: 'bot',
          text: result.reply.text,
          quickReplies: result.reply.ended ? [] : result.reply.quickReplies,
          url: result.reply.url,
          isFallback: result.reply.isFallback,
        },
      ],
    })),
    on(ChatApiActions.sendFailed, (state): ChatbotState => ({ ...state, sending: false })),
  ),
});
