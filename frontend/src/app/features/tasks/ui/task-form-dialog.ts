import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { ProjectMember } from '../../projects/data-access/projects-api.service';
import { TASK_PRIORITIES, Task, TaskInput, TaskPriority } from '../data-access/tasks-api.service';

export interface TaskFormData {
  readonly task: Task | null;
  readonly members: readonly ProjectMember[];
  readonly readOnly: boolean;
  readonly save: SaveFn<TaskInput>;
}

/** The dialog saves by itself and closes without a result; it closes with this when the user asks to delete. */
export type TaskFormResult = 'deleteRequested';

/** Today in the browser's time zone as `yyyy-MM-dd`, the format of date inputs and of the API. */
export function localToday(now = new Date()): string {
  const offset = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offset).toISOString().slice(0, 10);
}

/**
 * Creates a task, or shows and edits the one passed in. Read-only for admins who aren't members. Stays open,
 * keeping the values, until the save succeeds.
 */
@Component({
  selector: 'app-task-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  templateUrl: './task-form-dialog.html',
  styles: `
    mat-form-field {
      width: 100%;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 0 12px;
    }
    .row mat-form-field {
      flex: 1 1 180px;
    }
    .spacer {
      flex: 1;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskFormDialog {
  private readonly dialogRef = inject<MatDialogRef<TaskFormDialog, TaskFormResult>>(MatDialogRef);
  protected readonly data = inject<TaskFormData>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);
  protected readonly priorities = TASK_PRIORITIES;
  /** New tasks can't be due in the past; existing ones may keep a past date. */
  protected readonly minDate = this.data.task ? null : localToday();

  protected readonly assignees = this.assigneeOptions();

  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly form = this.fb.group({
    title: [this.data.task?.title ?? '', [Validators.required, Validators.maxLength(200)]],
    description: [this.data.task?.description ?? '', Validators.maxLength(4000)],
    priority: this.fb.control<TaskPriority>(this.data.task?.priority ?? 'Medium'),
    assigneeId: [this.data.task?.assigneeId ?? ''],
    dueDate: [this.data.task?.dueDate ?? ''],
  });

  constructor() {
    if (this.data.readOnly) {
      this.form.disable();
    }
  }

  protected submit(): void {
    const { dueDate } = this.form.getRawValue();
    if (this.minDate && dueDate && dueDate < this.minDate) {
      this.form.controls.dueDate.setErrors({ past: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { title, description, priority, assigneeId } = this.form.getRawValue();
    this.saving.set(true);
    this.data
      .save({
        title: title.trim(),
        description: description.trim() || null,
        priority,
        assigneeId: assigneeId || null,
        dueDate: dueDate || null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (error) {
          // A description of up to 4000 characters must not be lost to a rejected save.
          applyApiFieldErrors(this.form, error);
        } else {
          this.dialogRef.close();
        }
      });
  }

  protected delete(): void {
    this.dialogRef.close('deleteRequested');
  }

  /**
   * Active members, plus whoever the task is assigned to now: a deactivated member, or someone who left the
   * project (no display name; the template shows "former member").
   */
  private assigneeOptions(): { userId: string; displayName: string | null }[] {
    const current = this.data.task?.assigneeId ?? null;
    const options: { userId: string; displayName: string | null }[] = this.data.members
      .filter((member) => member.isActive || member.userId === current)
      .map(({ userId, displayName }) => ({ userId, displayName }));
    if (current && !options.some((option) => option.userId === current)) {
      options.push({ userId: current, displayName: null });
    }
    return options;
  }
}
