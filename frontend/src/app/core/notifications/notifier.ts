import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { ApiError } from '../http/api-error';

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
    const key = `apiErrors.${error.code}`;
    const message = this.transloco.translate(key);
    return message === key ? this.transloco.translate('apiErrors.unexpected') : message;
  }
}
