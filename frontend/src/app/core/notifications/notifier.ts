import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { ApiError } from '@shared/data-access/api-error';
import { translateOr } from '../i18n/error-text.pipe';

/** Short messages at the bottom of the screen, for results that don't belong in a form. */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  success(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 3000 });
  }

  /** A neutral message by translation key (for example, a resource that is gone). */
  info(key: string): void {
    this.snackBar.open(this.transloco.translate(key), this.transloco.translate('common.dismiss'), { duration: 6000 });
  }

  /** A problem that isn't an API answer (a load that failed in the background), by translation key. */
  warning(key: string): void {
    this.snackBar.open(this.transloco.translate(key), this.transloco.translate('common.dismiss'), { duration: 8000 });
  }

  error(error: ApiError): void {
    this.snackBar.open(this.errorMessage(error), this.transloco.translate('common.dismiss'), { duration: 8000 });
  }

  /**
   * The translation of the error code, or the generic message for a code the frontend doesn't know yet. An
   * unexpected failure that has a trace id adds its first 8 characters as a support code the user can report.
   */
  errorMessage(error: ApiError): string {
    const text = translateOr(this.transloco, `apiErrors.${error.code}`, 'apiErrors.unexpected');
    if (error.code !== 'unexpected' || !error.traceId) {
      return text;
    }
    return `${text} ${this.transloco.translate('common.supportCode', { code: error.traceId.slice(0, 8) })}`;
  }
}
