import { HttpErrorResponse } from '@angular/common/http';

/**
 * A failed API call reduced to what the UI needs. `code` is the API's error code (`Projects.LastOwner`), or one
 * of `validation`, `forbidden`, `network` and `unexpected`; it doubles as the key under `apiErrors` in the
 * translation files. `fields` holds the field names of a validation error, with the API's (localized) messages.
 */
export interface ApiError {
  code: string;
  fields: Record<string, string[]>;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { code: 'unexpected', fields: {} };
  }

  const body = error.error as { code?: string; errors?: Record<string, string[]> } | null;
  if (body?.code) {
    return { code: body.code, fields: {} };
  }
  if (error.status === 400 && body?.errors) {
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
