import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TRACE_ID, formatTraceparent, newSpanId, newTraceId, rememberTraceId } from '@shared/data-access/trace';

const apiPrefix = `${environment.apiUrl}/api/`;

/**
 * Sends a W3C `traceparent` with every API call so the backend's spans join the browser's request. It runs after
 * `authInterceptor` in the chain, so the retry that follows a token refresh passes through it again: it keeps the
 * trace id (kept in the request context) and gets a new span id.
 */
export const traceparentInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(apiPrefix)) {
    return next(request);
  }

  const traceId = request.context.get(TRACE_ID) ?? newTraceId();
  request.context.set(TRACE_ID, traceId);

  return next(request.clone({ setHeaders: { traceparent: formatTraceparent(traceId, newSpanId()) } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        rememberTraceId(error, traceId);
      }
      return throwError(() => error);
    }),
  );
};
