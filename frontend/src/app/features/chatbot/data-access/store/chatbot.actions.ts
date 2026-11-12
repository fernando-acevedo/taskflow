import { createActionGroup, props } from '@ngrx/store';
import { SendChatMessageResult } from '@core/api/types';
import { ApiError } from '@shared/data-access/api-error';

export const ChatWidgetActions = createActionGroup({
  source: 'Chat Widget',
  events: {
    /** Starts a new session (empty turn) in `locale`; also used by "Start over" and "Restart in <language>". */
    'Session Requested': props<{ locale: string }>(),
    'Message Sent': props<{ text: string }>(),
    'Quick Reply Selected': props<{ id: string; label: string }>(),
  },
});

export const ChatApiActions = createActionGroup({
  source: 'Chat API',
  events: {
    'Reply Received': props<{ result: SendChatMessageResult }>(),
    'Send Failed': props<{ error: ApiError }>(),
  },
});
