import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProjectMember } from '../../projects/data-access/projects-api.service';
import { TASK_PRIORITIES, Task, TaskInput, TaskPriority } from '../data-access/tasks-api.service';

export interface TaskFormData {
  task: Task | null;
  members: ProjectMember[];
  readOnly: boolean;
}

export type TaskFormResult = { action: 'save'; task: TaskInput } | { action: 'delete' };

/** Today in the browser's time zone as `yyyy-MM-dd`, the format of date inputs and of the API. */
export function localToday(now = new Date()): string {
  const offset = now.getTimezoneOffset() * 60_000;
  return new Date(now.getTime() - offset).toISOString().slice(0, 10);
}

/** Creates a task, or shows and edits the one passed in. Read-only for admins who aren't members. */
@Component({
  selector: 'app-task-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
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
  protected readonly priorities = TASK_PRIORITIES;
  /** New tasks can't be due in the past; existing ones may keep a past date. */
  protected readonly minDate = this.data.task ? null : localToday();

  protected readonly assignees = this.assigneeOptions();

  protected readonly form = inject(NonNullableFormBuilder).group({
    title: [this.data.task?.title ?? '', [Validators.required, Validators.maxLength(200)]],
    description: [this.data.task?.description ?? '', Validators.maxLength(4000)],
    priority: [this.data.task?.priority ?? ('Medium' as TaskPriority)],
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
    this.dialogRef.close({
      action: 'save',
      task: {
        title: title.trim(),
        description: description.trim() || null,
        priority,
        assigneeId: assigneeId || null,
        dueDate: dueDate || null,
      },
    });
  }

  protected delete(): void {
    this.dialogRef.close({ action: 'delete' });
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
