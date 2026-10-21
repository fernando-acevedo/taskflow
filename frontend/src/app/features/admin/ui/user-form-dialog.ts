import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { NewUserInput } from '../data-access/admin-users-api.service';

/**
 * Creates an account; the API emails the user a link to choose their password. Saves through the function
 * passed as data and stays open until the API accepts the user.
 */
@Component({
  selector: 'app-user-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'users'">
      <h2 mat-dialog-title>{{ t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('email') }}</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="off" required cdkFocusInitial />
            @if (form.controls.email.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.email.invalid) {
              <mat-error>{{ t('emailRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('displayName') }}</mat-label>
            <input matInput formControlName="displayName" autocomplete="off" required />
            @if (form.controls.displayName.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.displayName.invalid) {
              <mat-error>{{ t('displayNameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-checkbox formControlName="isAdmin">{{ t('createAsAdmin') }}</mat-checkbox>
          <p class="hint">{{ t('createHint') }}</p>
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
          <button mat-flat-button type="submit" [disabled]="saving()">{{ t('createSubmit') }}</button>
        </mat-dialog-actions>
      </form>
    </ng-container>
  `,
  styles: `
    mat-form-field {
      width: 100%;
    }

    .hint {
      margin: 8px 0 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserFormDialog {
  private readonly dialogRef = inject<MatDialogRef<UserFormDialog, true>>(MatDialogRef);
  private readonly save = inject<SaveFn<NewUserInput>>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    isAdmin: [false],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, displayName, isAdmin } = this.form.getRawValue();
    this.saving.set(true);
    this.save({ email: email.trim(), displayName: displayName.trim(), isAdmin })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (error) {
          // The snackbar explains the error; the values stay so the admin can correct them.
          applyApiFieldErrors(this.form, error);
        } else {
          this.dialogRef.close(true);
        }
      });
  }
}
