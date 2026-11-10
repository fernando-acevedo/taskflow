import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { defer, retry, throwError, timer } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OnlineStatus } from '../network/online-status';

const apiPrefix = `${environment.apiUrl}/api/`;

/** Total tries for one request, the first included. */
export const MAX_ATTEMPTS = 3;
export const BASE_DELAY_MS = 500;

/** 0 is "the API couldn't be reached"; the others come from a gateway or a server that is starting or overloaded. */
const retryableStatuses = new Set([0, 502, 503, 504]);

/** Waits `BASE_DELAY_MS * 2^(retry - 1)`, randomly shortened by up to half (jitter), so clients don't retry in step. */
export function backoffDelay(retryNumber: number, random: () => number = Math.random): number {
  const ceiling = BASE_DELAY_MS * 2 ** (retryNumber - 1);
  return Math.round(ceiling / 2 + (ceiling / 2) * random());
}

/**
 * Sends an API GET again when the network or a gateway fails (status 0, 502, 503, 504), up to `MAX_ATTEMPTS` tries
 * with exponential backoff and jitter (and not at all while the browser reports it is offline: the banner already says so, and a retry would only wait for nothing). Other methods are never repeated (a POST could be applied twice) and neither
 * are 4xx answers (the request is wrong, not unlucky).
 *
 * Order in the chain: after `authInterceptor`, so a 401 is refreshed there and a refresh isn't counted as a try,
 * and before `traceparentInterceptor`, so every try goes through it again with the same trace id and a new span id.
 */
export const retryInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.method !== 'GET' || !request.url.startsWith(apiPrefix)) {
    return next(request);
  }

  const online = inject(OnlineStatus);

  // `defer`: each try runs the rest of the chain again (a plain re-subscription would resend the same clone,
  // with the same span id).
  return defer(() => next(request)).pipe(
    retry({
      count: MAX_ATTEMPTS - 1,
      delay: (error: unknown, retryNumber) =>
        error instanceof HttpErrorResponse && retryableStatuses.has(error.status) && online.online()
          ? timer(backoffDelay(retryNumber))
          : throwError(() => error),
    }),
  );
};
