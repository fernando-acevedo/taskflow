import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { Label, LabelInput } from '../data-access/labels-api.service';
import { LabelChip } from './label-chip';
import { LABEL_COLORS, textColorFor } from './label-colors';

export interface LabelFormData {
  readonly label: Label | null;
  readonly save: SaveFn<LabelInput>;
}

/** Creates a label, or edits the one passed in: a name and a color from the fixed palette. */
@Component({
  selector: 'app-label-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
    LabelChip,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'labels'">
      <h2 mat-dialog-title>{{ data.label ? t('editTitle') : t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" required maxlength="40" />
            @if (form.controls.name.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>

          <div role="radiogroup" [attr.aria-label]="t('color')" class="palette">
            @for (color of colors; track color) {
              <button
                type="button"
                role="radio"
                class="swatch"
                [style.background-color]="color"
                [style.color]="textColor(color)"
                [attr.aria-checked]="form.controls.color.value === color"
                [attr.aria-label]="color"
                (click)="form.controls.color.setValue(color)"
              >
                @if (form.controls.color.value === color) {
                  <mat-icon aria-hidden="true">check</mat-icon>
                }
              </button>
            }
          </div>
          @if (form.controls.color.getError('api'); as message) {
            <p class="error" role="alert">{{ message }}</p>
          }
          <p class="preview">
            {{ t('preview') }}
            <app-label-chip [label]="{ name: form.controls.name.value || t('name'), color: form.controls.color.value }" />
          </p>
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
    .palette {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .swatch {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      border: 2px solid var(--mat-sys-outline);
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 0;
    }
    .swatch:focus-visible {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }
    .error {
      color: var(--mat-sys-error);
      font: var(--mat-sys-body-small);
    }
    .preview {
      display: flex;
      align-items: center;
      gap: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabelFormDialog {
  private readonly dialogRef = inject<MatDialogRef<LabelFormDialog, true>>(MatDialogRef);
  protected readonly data = inject<LabelFormData>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);
  protected readonly colors = LABEL_COLORS;
  protected readonly textColor = textColorFor;

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.data.label?.name ?? '', [Validators.required, Validators.maxLength(40)]],
    color: [this.data.label?.color ?? LABEL_COLORS[5]],
  });

  protected submit(): void {
    const { name, color } = this.form.getRawValue();
    if (!name.trim()) {
      this.form.controls.name.setErrors({ required: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.data
      .save({ name: name.trim(), color })
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
