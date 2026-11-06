import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { MAX_ENTRY_MINUTES, parseDuration, toClockText } from '../../../shared/ui/duration';
import { localToday } from '../../../shared/ui/local-today';
import { NOTE_MAX_LENGTH, TimeEntry, TimeEntryInput } from '../data-access/time-tracking-api.service';

/**
 * A small form to log time, new or being edited: date (today by default, never in the future), duration
 * ("2:30", "2h 30m" or decimal hours) and an optional note. Keeps what was typed until the save succeeds.
 */
@Component({
  selector: 'app-time-entry-form',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'time'">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="fields">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ t('date') }}</mat-label>
            <input matInput type="date" formControlName="date" [max]="today" required />
            @if (form.controls.date.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.date.invalid) {
              <mat-error>{{ t('dateRule') }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ t('duration') }}</mat-label>
            <input matInput formControlName="minutes" inputmode="decimal" autocomplete="off" required />
            @if (form.controls.minutes.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.minutes.invalid) {
              <mat-error>{{ t('durationRule') }}</mat-error>
            } @else {
              <mat-hint>{{ t('durationHint') }}</mat-hint>
            }
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ t('note') }}</mat-label>
          <input matInput formControlName="note" autocomplete="off" />
          @if (form.controls.note.getError('api'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.note.hasError('maxlength')) {
            <mat-error>{{ t('noteRule', { max: noteMax }) }}</mat-error>
          }
        </mat-form-field>

        <div class="actions">
          @if (cancellable()) {
            <button mat-button type="button" [disabled]="saving()" (click)="cancelled.emit()">{{ t('cancel') }}</button>
          }
          <button mat-flat-button type="submit" [disabled]="saving()">{{ t(submitLabel()) }}</button>
        </div>
      </form>
    </ng-container>
  `,
  styles: `
    form {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .fields {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .fields mat-form-field {
      flex: 1 1 160px;
    }
    mat-form-field {
      width: 100%;
    }
    .actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimeEntryForm implements OnInit {
  readonly save = input.required<SaveFn<TimeEntryInput>>();
  /** The entry being edited; null logs a new one. */
  readonly entry = input<TimeEntry | null>(null);
  readonly submitLabel = input('log');
  readonly cancellable = input(false);
  readonly saved = output();
  readonly cancelled = output();

  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly saving = signal(false);
  protected readonly noteMax = NOTE_MAX_LENGTH;
  protected readonly today = localToday();

  protected readonly form = this.fb.group({
    date: [this.today, Validators.required],
    minutes: ['', Validators.required],
    note: ['', Validators.maxLength(NOTE_MAX_LENGTH)],
  });

  ngOnInit(): void {
    const entry = this.entry();
    if (entry) {
      this.form.setValue({ date: entry.date, minutes: toClockText(entry.minutes), note: entry.note ?? '' });
    }
  }

  protected submit(): void {
    const { date, minutes: text, note } = this.form.getRawValue();
    const minutes = parseDuration(text);
    if (minutes === null || minutes < 1 || minutes > MAX_ENTRY_MINUTES) {
      this.form.controls.minutes.setErrors({ duration: true });
    }
    if (date > this.today) {
      this.form.controls.date.setErrors({ future: true });
    }
    if (this.form.invalid || minutes === null) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.save()({ date, minutes, note: note.trim() || null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (error) {
          applyApiFieldErrors(this.form, error);
        } else {
          if (!this.entry()) {
            // Ready for the next entry; the date stays, since people log several in one go.
            this.form.patchValue({ minutes: '', note: '' });
            this.form.markAsUntouched();
          }
          this.saved.emit();
        }
      });
  }
}
