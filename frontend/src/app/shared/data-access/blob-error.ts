import { HttpErrorResponse } from '@angular/common/http';

/**
 * A failed `responseType: 'blob'` call carries its ProblemDetails as a Blob; read it as JSON so that the error
 * keeps its code and `toApiError` can translate it.
 */
export async function readBlobError(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) {
    return error;
  }
  let body: unknown = null;
  try {
    body = JSON.parse(await error.error.text());
  } catch {
    // Not JSON: keep the status only.
  }
  return new HttpErrorResponse({
    status: error.status,
    statusText: error.statusText,
    url: error.url ?? undefined,
    headers: error.headers,
    error: body,
  });
}
