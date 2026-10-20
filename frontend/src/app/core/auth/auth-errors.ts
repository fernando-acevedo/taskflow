import { HttpErrorResponse } from '@angular/common/http';
import { problemBody } from '../http/api-error';
import { LoginErrorCode } from './auth.models';

/** The API rejected the refresh token itself; any other failure (network, 5xx) leaves the session alone. */
export function isTokenRejected(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401);
}

/** Identity answers a failed login with 401 and detail "Failed" or "LockedOut" (deactivated accounts are locked out). */
export function toLoginError(error: unknown): LoginErrorCode {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 429) {
      return 'tooManyAttempts';
    }
    if (error.status === 401) {
      return problemBody(error).detail === 'LockedOut' ? 'lockedOut' : 'invalidCredentials';
    }
  }
  return 'unexpected';
}

/** A 400 ValidationProblem is keyed by Identity error code (register) or by field name (profile). */
export function toFormErrors(error: unknown): string[] {
  if (error instanceof HttpErrorResponse) {
    const errors = problemBody(error).errors;
    if (error.status === 400 && errors) {
      return Object.keys(errors);
    }
    if (error.status === 429) {
      return ['tooManyAttempts'];
    }
  }
  return ['unexpected'];
}
