import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { Client, ClientInput } from '../data-access/clients-api.service';

export interface ClientFormData {
  readonly client: Client | null;
  readonly save: SaveFn<ClientInput>;
}

/** Creates a client, or edits the one passed as data. Stays open, keeping the values, until the save succeeds. */
@Component({
  selector: 'app-client-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'clients'">
      <h2 mat-dialog-title>{{ client ? t('editTitle') : t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" required cdkFocusInitial />
            @if (form.controls.name.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('contactEmail') }}</mat-label>
            <input matInput type="email" formControlName="contactEmail" autocomplete="off" />
            @if (form.controls.contactEmail.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.contactEmail.invalid) {
              <mat-error>{{ t('emailRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('notes') }}</mat-label>
            <textarea matInput formControlName="notes" rows="3"></textarea>
            @if (form.controls.notes.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            }
          </mat-form-field>
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
          <button mat-flat-button type="submit" [disabled]="saving()">{{ t('save') }}</button>
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
  private readonly dialogRef = inject<MatDialogRef<ClientFormDialog, true>>(MatDialogRef);
  private readonly data = inject<ClientFormData>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly client = this.data.client;
  protected readonly saving = signal(false);

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
    this.saving.set(true);
    this.data
      .save({ name: name.trim(), contactEmail: contactEmail.trim() || null, notes: notes.trim() || null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (error) {
          applyApiFieldErrors(this.form, error);
        } else {
          this.dialogRef.close(true);
        }
      });
  }
}
