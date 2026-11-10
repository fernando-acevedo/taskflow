import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '@shared/data-access/api-field-errors';
import { SaveFn } from '@shared/data-access/submission';
import { TenantRole } from '@core/tenant/tenant.models';
import { NewMemberInput } from '../data-access/tenant-members-api.service';
import { assignableRoles } from './member-permissions';
import { confirmCloseWhenModified } from '@shared/ui/unsaved-changes';

/**
 * Adds a person to the tenant (creating their account when needed; the API emails a link to choose a password).
 * Saves through the function passed as data and stays open until the API accepts them.
 */
@Component({
  selector: 'app-member-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
    TranslocoDirective,
  ],
  template: `
    <ng-container *transloco="let tr">
      <ng-container *transloco="let t; prefix: 'users'">
        <h2 mat-dialog-title>{{ t('createTitle') }}</h2>
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <mat-dialog-content>
            @if (saving()) {
              <mat-progress-bar mode="indeterminate" />
            }
            <mat-form-field appearance="outline">
              <mat-label>{{ t('email') }}</mat-label>
              <input
                matInput
                type="email"
                formControlName="email"
                autocomplete="off"
                required
                cdkFocusInitial
              />
              @if (form.controls.email.getError('api'); as message) {
                <mat-error>{{ message }}</mat-error>
              } @else if (form.controls.email.invalid) {
                <mat-error>{{ t('emailRule') }}</mat-error>
              }
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ t('displayName') }}</mat-label>
              <input matInput formControlName="displayName" autocomplete="off" />
              <mat-hint>{{ t('displayNameHint') }}</mat-hint>
              @if (form.controls.displayName.getError('api'); as message) {
                <mat-error>{{ message }}</mat-error>
              } @else if (form.controls.displayName.invalid) {
                <mat-error>{{ t('displayNameRule') }}</mat-error>
              }
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ t('role') }}</mat-label>
              <mat-select formControlName="role" required>
                @for (role of roles; track role) {
                  <mat-option [value]="role">{{ tr('tenant.roles.' + role) }}</mat-option>
                }
              </mat-select>
              <mat-hint>{{ t('roleHint') }}</mat-hint>
            </mat-form-field>
            <p class="hint">{{ t('createHint') }}</p>
          </mat-dialog-content>
          <mat-dialog-actions align="end">
            <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
            <button mat-flat-button type="submit" [disabled]="saving()">
              {{ t('createSubmit') }}
            </button>
          </mat-dialog-actions>
        </form>
      </ng-container>
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
export class MemberFormDialog {
  private readonly dialogRef = inject<MatDialogRef<MemberFormDialog, true>>(MatDialogRef);
  private readonly data = inject<{ save: SaveFn<NewMemberInput>; isOwner: boolean }>(
    MAT_DIALOG_DATA,
  );
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);
  protected readonly roles: TenantRole[] = assignableRoles(this.data.isOwner);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    displayName: ['', [Validators.minLength(2), Validators.maxLength(100)]],
    role: ['Member' as TenantRole, [Validators.required]],
  });

  constructor() {
    confirmCloseWhenModified(this.dialogRef, () => this.form.dirty);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, displayName, role } = this.form.getRawValue();
    this.saving.set(true);
    this.data
      .save({
        email: email.trim(),
        role,
        ...(displayName.trim() ? { displayName: displayName.trim() } : {}),
      })
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
