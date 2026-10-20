import { HttpErrorResponse } from '@angular/common/http';
import { LoginErrorCode } from './auth.models';

/** Identity answers a failed login with 401 and detail "Failed" or "LockedOut" (deactivated accounts are locked out). */
export function toLoginError(error: unknown): LoginErrorCode {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 429) {
      return 'tooManyAttempts';
    }
    if (error.status === 401) {
      return error.error?.detail === 'LockedOut' ? 'lockedOut' : 'invalidCredentials';
    }
  }
  return 'unexpected';
}

/** A 400 ValidationProblem is keyed by Identity error code (register) or by field name (profile). */
export function toFormErrors(error: unknown): string[] {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 400 && error.error?.errors) {
      return Object.keys(error.error.errors);
    }
    if (error.status === 429) {
      return ['tooManyAttempts'];
    }
  }
  return ['unexpected'];
}
