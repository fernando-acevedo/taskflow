import { Action } from '@ngrx/store';

const SECRET_KEYS = new Set(['password', 'token', 'accessToken', 'refreshToken']);
const MASK = '••••••';
const TOKEN_IN_URL = /((?:^|[?&#])token=)[^&#]*/g;

/** Masks secrets by key at any depth, and the invitation token inside router URLs and fragments. */
export function maskSecrets(value: unknown, key?: string): unknown {
  if (typeof value === 'string') {
    return key === 'url' || key === 'fragment' ? value.replace(TOKEN_IN_URL, `$1${MASK}`) : value;
  }
  if (Array.isArray(value)) {
    return value.map((item) => maskSecrets(item));
  }
  if (value === null || typeof value !== 'object') {
    return value;
  }
  return Object.fromEntries(
    Object.entries(value).map(([innerKey, inner]) => [
      innerKey,
      SECRET_KEYS.has(innerKey) ? MASK : maskSecrets(inner, innerKey),
    ]),
  );
}

/** Keeps passwords and tokens out of the Redux DevTools action log. */
export function hideSecrets(action: Action): Action {
  return maskSecrets(action) as Action;
}

/** The same for the state DevTools shows, where the router keeps the current URL and its query parameters. */
export function hideStateSecrets(state: unknown): unknown {
  return maskSecrets(state);
}
