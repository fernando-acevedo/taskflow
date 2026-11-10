import { HttpContextToken, HttpErrorResponse } from '@angular/common/http';

/** The trace id of a request (shared by its clones, so a retry after a token refresh stays in the same trace). */
export const TRACE_ID = new HttpContextToken<string | null>(() => null);

const traceIds = new WeakMap<HttpErrorResponse, string>();

/** Remembers the trace id sent with the request that failed with `error`, for `toApiError`. */
export function rememberTraceId(error: HttpErrorResponse, traceId: string): void {
  traceIds.set(error, traceId);
}

export function sentTraceId(error: HttpErrorResponse): string | undefined {
  return traceIds.get(error);
}

function randomHex(bytes: number): string {
  const values = crypto.getRandomValues(new Uint8Array(bytes));
  // An all-zero id is invalid in W3C Trace Context.
  if (values.every((value) => value === 0)) {
    values[bytes - 1] = 1;
  }
  return Array.from(values, (value) => value.toString(16).padStart(2, '0')).join('');
}

export const newTraceId = (): string => randomHex(16);
export const newSpanId = (): string => randomHex(8);

export const formatTraceparent = (traceId: string, spanId: string): string => `00-${traceId}-${spanId}-01`;
