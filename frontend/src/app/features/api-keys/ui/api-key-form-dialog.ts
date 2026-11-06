import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { localToday } from '../../../shared/ui/local-today';
import { API_KEY_SCOPES, ApiKeyScope, NewApiKeyInput } from '../data-access/api-keys-api.service';

export interface ApiKeyFormData {
  readonly save: SaveFn<NewApiKeyInput>;
}

/** The longest an API key can last, in days. */
const MAX_DAYS = 365;

/** Adds `days` to a `yyyy-MM-dd` date. */
function addDays(date: string, days: number): string {
  const result = new Date(`${date}T00:00:00Z`);
  result.setUTCDate(result.getUTCDate() + days);
  return result.toISOString().slice(0, 10);
}

/** Asks for the name, scope and optional expiry of a new key; stays open until the API accepts it. */
@Component({
  selector: 'app-api-key-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'profile.apiKeys'">
      <h2 mat-dialog-title>{{ t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" autocomplete="off" required cdkFocusInitial />
            @if (form.controls.name.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('scope') }}</mat-label>
            <mat-select formControlName="scope" required>
              @for (scope of scopes; track scope) {
                <mat-option [value]="scope">{{ t('scopes.' + scope) }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('expiresAt') }}</mat-label>
            <input matInput type="date" formControlName="expiresAt" [min]="minDate" [max]="maxDate" />
            @if (form.controls.expiresAt.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.expiresAt.invalid) {
              <mat-error>{{ t('expiresRule') }}</mat-error>
            } @else {
              <mat-hint>{{ t('expiresHint') }}</mat-hint>
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
export class ApiKeyFormDialog {
  private readonly dialogRef = inject<MatDialogRef<ApiKeyFormDialog, true>>(MatDialogRef);
  private readonly data = inject<ApiKeyFormData>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);
  protected readonly scopes = API_KEY_SCOPES;
  protected readonly minDate = addDays(localToday(), 1);
  protected readonly maxDate = addDays(localToday(), MAX_DAYS);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/\S/)]],
    scope: new FormControl<ApiKeyScope>('Read', { nonNullable: true }),
    expiresAt: [''],
  });

  protected submit(): void {
    const { name, scope, expiresAt } = this.form.getRawValue();
    if (expiresAt && (expiresAt < this.minDate || expiresAt > this.maxDate)) {
      this.form.controls.expiresAt.setErrors({ range: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.data
      .save({ name: name.trim(), scope, expiresAt: expiresAt || null })
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
