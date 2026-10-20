import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslocoDirective } from '@jsverse/transloco';
import { Observable, map } from 'rxjs';

export interface ConfirmDialogData {
  /** Translation keys. */
  title: string;
  message: string;
  confirm: string;
  params?: Record<string, unknown>;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatButtonModule, MatDialogModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <h2 mat-dialog-title>{{ t(data.title, data.params) }}</h2>
      <mat-dialog-content>{{ t(data.message, data.params) }}</mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" [mat-dialog-close]="false">{{ t('common.cancel') }}</button>
        <button mat-flat-button type="button" [mat-dialog-close]="true">{{ t(data.confirm) }}</button>
      </mat-dialog-actions>
    </ng-container>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}

/** Opens the dialog and emits once: true when the user confirmed. */
export function confirm(dialog: MatDialog, data: ConfirmDialogData): Observable<boolean> {
  return dialog
    .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data, width: '420px' })
    .afterClosed()
    .pipe(map((confirmed) => confirmed === true));
}
