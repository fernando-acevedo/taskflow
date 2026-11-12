import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { catchError, concatMap, filter, map, of, tap, withLatestFrom } from 'rxjs';
import { Notifier } from '@core/notifications/notifier';
import { toApiError } from '@shared/data-access/api-error';
import { ChatTurn, ChatbotApiService } from '../chatbot-api.service';
import { ChatApiActions, ChatWidgetActions } from './chatbot.actions';
import { chatbotFeature } from './chatbot.feature';

/** Every turn goes out in order (none is dropped); the server fixes the language per session. */
export const sendTurn = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ChatbotApiService)) =>
    actions$.pipe(
      ofType(ChatWidgetActions.sessionRequested, ChatWidgetActions.messageSent, ChatWidgetActions.quickReplySelected),
      withLatestFrom(store.select(chatbotFeature.selectSessionId), store.select(chatbotFeature.selectLocale)),
      concatMap(([action, sessionId, locale]) => {
        let turn: ChatTurn;
        if (action.type === ChatWidgetActions.sessionRequested.type) {
          turn = { locale: action.locale };
        } else if (action.type === ChatWidgetActions.messageSent.type) {
          turn = { sessionId, locale, text: action.text };
        } else {
          turn = { sessionId, locale, quickReplyId: action.id };
        }
        return api.send(turn).pipe(
          map((result) => ChatApiActions.replyReceived({ result })),
          catchError((error: unknown) => of(ChatApiActions.sendFailed({ error: toApiError(error) }))),
        );
      }),
    ),
  { functional: true },
);

export const showSendError = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier)) =>
    actions$.pipe(
      ofType(ChatApiActions.sendFailed),
      tap(({ error }) => notifier.error(error)),
    ),
  { functional: true, dispatch: false },
);

/** The server no longer knows that quick reply (the script changed): begin again in the same language. */
export const restartOnUnknownQuickReply = createEffect(
  (actions$ = inject(Actions), store = inject(Store)) =>
    actions$.pipe(
      ofType(ChatApiActions.sendFailed),
      filter(({ error }) => error.code === 'Chatbot.UnknownQuickReply'),
      withLatestFrom(store.select(chatbotFeature.selectLocale)),
      map(([, locale]) => ChatWidgetActions.sessionRequested({ locale: locale ?? 'en' })),
    ),
  { functional: true },
);
