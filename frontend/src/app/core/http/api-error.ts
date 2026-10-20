import { HttpErrorResponse } from '@angular/common/http';

/**
 * A failed API call reduced to what the UI needs. `code` is the API's error code (`Projects.LastOwner`), or one
 * of `validation`, `forbidden`, `network` and `unexpected`; it doubles as the key under `apiErrors` in the
 * translation files. `fields` holds the field names of a validation error, with the API's (localized) messages.
 */
export interface ApiError {
  readonly code: string;
  readonly fields: Record<string, string[]>;
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
}

export function problemBody(error: HttpErrorResponse): ProblemBody {
  const body: unknown = error.error;
  if (typeof body !== 'object' || body === null) {
    return {};
  }

  const { code, detail, errors } = body as Record<string, unknown>;
  return {
    code: typeof code === 'string' ? code : undefined,
    detail: typeof detail === 'string' ? detail : undefined,
    errors: typeof errors === 'object' && errors !== null ? (errors as Record<string, string[]>) : undefined,
  };
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { code: 'unexpected', fields: {} };
  }

  const body = problemBody(error);
  if (body.code) {
    return { code: body.code, fields: {} };
  }
  if (error.status === 400 && body.errors) {
    return { code: 'validation', fields: body.errors };
  }
  if (error.status === 403) {
    return { code: 'forbidden', fields: {} };
  }
  if (error.status === 0) {
    return { code: 'network', fields: {} };
  }
  return { code: 'unexpected', fields: {} };
}
