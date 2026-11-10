import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from './api-error';
import { rememberTraceId } from './trace';

describe('toApiError', () => {
  it('uses the error code the API sends in the problem details', () => {
    const error = new HttpErrorResponse({ status: 409, error: { title: 'A project needs at least one owner.', code: 'Projects.LastOwner' } });

    expect(toApiError(error)).toEqual({ code: 'Projects.LastOwner', fields: {} });
  });

  it('keeps the field messages of a validation problem', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: { name: ['Too short.'] } } });

    expect(toApiError(error)).toEqual({ code: 'validation', fields: { name: ['Too short.'] } });
  });

  it('names forbidden, network and unknown failures', () => {
    expect(toApiError(new HttpErrorResponse({ status: 403 })).code).toBe('forbidden');
    expect(toApiError(new HttpErrorResponse({ status: 0 })).code).toBe('network');
    expect(toApiError(new HttpErrorResponse({ status: 500 })).code).toBe('unexpected');
    expect(toApiError(new Error('boom')).code).toBe('unexpected');
  });

  it('takes the trace id from the problem details, or from the traceparent that was sent', () => {
    const fromApi = new HttpErrorResponse({ status: 500, error: { traceId: 'abc123def456' } });
    expect(toApiError(fromApi).traceId).toBe('abc123def456');

    const sent = new HttpErrorResponse({ status: 500 });
    rememberTraceId(sent, 'feedfacefeedface');
    expect(toApiError(sent).traceId).toBe('feedfacefeedface');

    expect(toApiError(new HttpErrorResponse({ status: 500 })).traceId).toBeUndefined();
  });
});
