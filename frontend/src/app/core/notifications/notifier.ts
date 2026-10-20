import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { ApiError } from '../http/api-error';
import { translateOr } from '../i18n/error-text.pipe';

/** Short messages at the bottom of the screen, for results that don't belong in a form. */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  success(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 3000 });
  }

  error(error: ApiError): void {
    this.snackBar.open(this.errorMessage(error), this.transloco.translate('common.dismiss'), { duration: 8000 });
  }

  /** The translation of the error code, or the generic message for a code the frontend doesn't know yet. */
  errorMessage(error: ApiError): string {
    return translateOr(this.transloco, `apiErrors.${error.code}`, 'apiErrors.unexpected');
  }
}
