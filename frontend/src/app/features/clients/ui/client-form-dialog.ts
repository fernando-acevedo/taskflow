import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import { Client, ClientInput } from '../data-access/clients-api.service';

/** Creates a client, or edits the one passed as data. Closes with the entered values. */
@Component({
  selector: 'app-client-form-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'clients'">
      <h2 mat-dialog-title>{{ client ? t('editTitle') : t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" required cdkFocusInitial />
            @if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('contactEmail') }}</mat-label>
            <input matInput type="email" formControlName="contactEmail" autocomplete="off" />
            @if (form.controls.contactEmail.invalid) {
              <mat-error>{{ t('emailRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('notes') }}</mat-label>
            <textarea matInput formControlName="notes" rows="3"></textarea>
          </mat-form-field>
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
          <button mat-flat-button type="submit">{{ t('save') }}</button>
        </mat-dialog-actions>
      </form>
    </ng-container>
  `,
  styles: `
    mat-form-field {
      width: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientFormDialog {
  private readonly dialogRef = inject<MatDialogRef<ClientFormDialog, ClientInput>>(MatDialogRef);
  protected readonly client = inject<Client | null>(MAT_DIALOG_DATA);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.client?.name ?? '', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    contactEmail: [this.client?.contactEmail ?? '', [Validators.email, Validators.maxLength(256)]],
    notes: [this.client?.notes ?? '', [Validators.maxLength(2000)]],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, contactEmail, notes } = this.form.getRawValue();
    this.dialogRef.close({ name: name.trim(), contactEmail: contactEmail.trim() || null, notes: notes.trim() || null });
  }
}
