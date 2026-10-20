import { Action } from '@ngrx/store';
import { Credentials } from './auth.models';

/** Keeps passwords out of the Redux DevTools action log. */
export function hidePasswords(action: Action): Action {
  const credentials = (action as Action & { credentials?: Credentials }).credentials;
  return credentials ? ({ ...action, credentials: { ...credentials, password: '••••••' } } as Action) : action;
}
