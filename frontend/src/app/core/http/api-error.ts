import { HttpErrorResponse } from '@angular/common/http';
import { sentTraceId } from './trace';

/**
 * A failed API call reduced to what the UI needs. `code` is the API's error code (`Projects.LastOwner`), or one
 * of `validation`, `forbidden`, `network` and `unexpected`; it doubles as the key under `apiErrors` in the
 * translation files. `fields` holds the field names of a validation error, with the API's (localized) messages.
 */
export interface ApiError {
  readonly code: string;
  readonly fields: Record<string, string[]>;
  /** The id of the request's trace (the API's `traceId`, or the one the browser sent); shown to the user for support. */
  readonly traceId?: string;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** The parts of a ProblemDetails body the frontend reads, narrowed from the untyped `HttpErrorResponse.error`. */
export interface ProblemBody {
  readonly code?: string;
  readonly detail?: string;
  readonly errors?: Record<string, string[]>;
  readonly traceId?: string;
}

export function problemBody(error: HttpErrorResponse): ProblemBody {
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return {};
  }

  const { code, detail, errors, traceId } = body as Record<string, unknown>;
  return {
    code: typeof code === 'string' ? code : undefined,
    detail: typeof detail === 'string' ? detail : undefined,
    errors: typeof errors === 'object' && errors !== null ? (errors as Record<string, string[]>) : undefined,
    traceId: typeof traceId === 'string' ? traceId : undefined,
  };
}

/** The API's code for an edit based on a version of the record that someone else has since changed (HTTP 409). */
export const STALE_CODE = 'Concurrency.Stale';

export function isStale(error: ApiError | null | undefined): boolean {
  return error?.code === STALE_CODE;
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { code: 'unexpected', fields: {} };
  }

  const body = problemBody(error);
  const traceId = body.traceId ?? sentTraceId(error);
  const withTrace = (apiError: ApiError): ApiError => (traceId ? { ...apiError, traceId } : apiError);

  if (body.code) {
    return withTrace({ code: body.code, fields: {} });
  }
  if (error.status === 400 && body.errors) {
    return withTrace({ code: 'validation', fields: body.errors });
  }
  if (error.status === 403) {
    return withTrace({ code: 'forbidden', fields: {} });
  }
  if (error.status === 0) {
    return { code: 'network', fields: {} };
  }
  return withTrace({ code: 'unexpected', fields: {} });
}
