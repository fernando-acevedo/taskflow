import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Observable } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { isStale } from '../../../core/http/api-error';
import { StaleNotice } from '../../../shared/ui/stale-notice';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { SaveFn } from '../../../shared/data-access/submission';
import { parseDuration, toClockText } from '../../../shared/ui/duration';
import { localToday } from '../../../shared/ui/local-today';
import { LabelSummary } from '../../labels/data-access/labels-api.service';
import { TaskHistory } from '../../activity/ui/task-history';
import { TaskComments } from '../../comments/ui/task-comments';
import { LabelChip } from '../../labels/ui/label-chip';
import { TaskTime } from '../../time-tracking/ui/task-time';
import { ProjectMember } from '../../projects/data-access/projects-api.service';
import { TASK_PRIORITIES, Task, TaskInput, TaskPriority } from '../data-access/tasks-api.service';

export interface TaskFormData {
  readonly task: Task | null;
  readonly members: readonly ProjectMember[];
  /** The project's labels. */
  readonly labels: readonly LabelSummary[];
  /** The project's tasks, to show the titles of the ones this task depends on. */
  readonly tasks?: readonly Task[];
  readonly readOnly: boolean;
  readonly save: SaveFn<TaskInput>;
  /** The server's current task (null if it can't be fetched); the store then holds its new row version. */
  readonly refresh?: () => Observable<Task | null>;
}

/** The dialog saves by itself and closes without a result; it closes with this when the user asks to delete. */
export type TaskFormResult = 'deleteRequested';

/** Far above any real estimate; keeps a typo from reaching the API as a huge number. */
const MAX_ESTIMATE_MINUTES = 9999 * 60;

/**
 * Creates a task, or shows and edits the one passed in. Read-only for admins who aren't members. Stays open,
 * keeping the values, until the save succeeds.
 */
@Component({
  selector: 'app-task-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    TranslocoDirective,
    LabelChip,
    StaleNotice,
    TaskComments,
    TaskHistory,
    TaskTime,
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
    .planning h3 {
      font: var(--mat-sys-title-small);
      margin: 16px 0 4px;
    }
    .planning dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
      margin: 0;
    }
    .planning dt {
      color: var(--mat-sys-on-surface-variant);
    }
    .planning dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .planning ul {
      margin: 0;
      padding-left: 16px;
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
  /** Someone else changed the task while this dialog was open. */
  protected readonly stale = signal(false);
  protected readonly priorities = TASK_PRIORITIES;
  /** New tasks can't be due in the past; existing ones may keep a past date. */
  protected readonly minDate = this.data.task ? null : localToday();

  protected readonly assignees = this.assigneeOptions();
  protected readonly language = inject(LanguageService);
  /** Titles of the tasks this one depends on; a deleted task's id shows as the id. */
  protected readonly dependencies = (this.data.task?.dependsOn ?? []).map(
    (id) => this.data.tasks?.find((task) => task.id === id)?.title ?? id,
  );

  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly form = this.fb.group({
    title: [this.data.task?.title ?? '', [Validators.required, Validators.maxLength(200)]],
    description: [this.data.task?.description ?? '', Validators.maxLength(4000)],
    priority: this.fb.control<TaskPriority>(this.data.task?.priority ?? 'Medium'),
    assigneeId: [this.data.task?.assigneeId ?? ''],
    startDate: [this.data.task?.startDate ?? ''],
    dueDate: [this.data.task?.dueDate ?? ''],
    isBlocked: [this.data.task?.isBlocked ?? false],
    statusNote: [this.data.task?.statusNote ?? '', Validators.maxLength(500)],
    labelIds: [this.data.task?.labels.map((label) => label.id) ?? []],
    /** Hours and minutes ("2:30", "2h 30m") or decimal hours ("2.5"); empty for no estimate. */
    estimatedMinutes: [toClockText(this.data.task?.estimatedMinutes ?? null)],
  });

  constructor() {
    if (this.data.readOnly) {
      this.form.disable();
    }
  }

  protected submit(): void {
    const { dueDate, startDate } = this.form.getRawValue();
    if (this.minDate && dueDate && dueDate < this.minDate) {
      this.form.controls.dueDate.setErrors({ past: true });
    }
    if (startDate && dueDate && startDate > dueDate) {
      this.form.controls.startDate.setErrors({ startAfterDue: true });
    }
    const estimateText = this.form.controls.estimatedMinutes.value.trim();
    const estimate = estimateText === '' ? null : parseDuration(estimateText);
    if (estimateText !== '' && (estimate === null || estimate > MAX_ESTIMATE_MINUTES)) {
      this.form.controls.estimatedMinutes.setErrors({ duration: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { title, description, priority, assigneeId, labelIds, isBlocked, statusNote } = this.form.getRawValue();
    this.saving.set(true);
    this.stale.set(false);
    this.data
      .save({
        title: title.trim(),
        description: description.trim() || null,
        priority,
        assigneeId: assigneeId || null,
        startDate: startDate || null,
        dueDate: dueDate || null,
        isBlocked,
        statusNote: statusNote.trim() || null,
        labelIds,
        estimatedMinutes: estimate,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (isStale(error)) {
          this.stale.set(true);
        } else if (error) {
          // A description of up to 4000 characters must not be lost to a rejected save.
          applyApiFieldErrors(this.form, error);
          if (error.code === 'Tasks.StartAfterDue') {
            // Not a field error for the API, but the user fixes it in the start date.
            this.form.controls.startDate.setErrors({ startAfterDue: true });
            this.form.controls.startDate.markAsTouched();
          }
        } else {
          this.dialogRef.close();
        }
      });
  }

  /** Replaces the form with the server's version, losing what was typed. */
  protected reload(): void {
    this.fetchLatest((task) => {
      this.form.reset({
        title: task.title,
        description: task.description ?? '',
        priority: task.priority,
        assigneeId: task.assigneeId ?? '',
        startDate: task.startDate ?? '',
        dueDate: task.dueDate ?? '',
        isBlocked: task.isBlocked,
        statusNote: task.statusNote ?? '',
        labelIds: task.labels.map((label) => label.id),
        estimatedMinutes: toClockText(task.estimatedMinutes),
      });
    });
  }

  /** Takes the server's row version, then saves the typed values again. */
  protected overwrite(): void {
    this.fetchLatest(() => this.submit());
  }

  private fetchLatest(then: (task: Task) => void): void {
    const refresh = this.data.refresh;
    if (!refresh) {
      return;
    }
    this.saving.set(true);
    refresh()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((task) => {
        this.saving.set(false);
        if (task) {
          this.stale.set(false);
          then(task);
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
