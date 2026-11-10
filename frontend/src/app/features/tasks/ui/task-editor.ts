import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Store } from '@ngrx/store';
import { Observable, filter, map, switchMap } from 'rxjs';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { labelsFeature } from '../../labels/data-access/store/labels.feature';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { Task, TaskInput } from '../data-access/tasks-api.service';
import { tasksFeature } from '../data-access/store/tasks.feature';
import { TasksPageActions } from '../data-access/store/tasks.actions';
import { TaskFormData, TaskFormDialog, TaskFormResult } from './task-form-dialog';

/**
 * Opens a task (or a new one) in the form dialog, which saves through this editor, and confirms a deletion.
 * Shared by the board and the list. It reads the open project's state, so it's provided by the projects route
 * (where that state is registered) rather than in root.
 */
@Injectable()
export class TaskEditor {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  private readonly members = this.store.selectSignal(projectsFeature.selectMembers);
  private readonly labels = this.store.selectSignal(labelsFeature.selectAll);
  private readonly allTasks = this.store.selectSignal(tasksFeature.selectAll);
  private readonly canEdit = this.store.selectSignal(projectsFeature.selectCanEditTasks);

  open(task: Task | null = null): void {
    const save: SaveFn<TaskInput> = (input) =>
      this.submissions.dispatchAndWait(
        task
          ? TasksPageActions.updateSubmitted({ taskId: task.id, changes: input, submissionId: nextSubmissionId() })
          : TasksPageActions.createSubmitted({ task: input, submissionId: nextSubmissionId() }),
      );

    const refresh = (): Observable<Task | null> =>
      this.submissions
        .dispatchAndWait(TasksPageActions.refreshRequested({ taskId: task?.id ?? '', submissionId: nextSubmissionId() }))
        .pipe(map((error) => (error ? null : (this.allTasks().find((t) => t.id === task?.id) ?? null))));

    const closed = this.dialog
      .open<TaskFormDialog, TaskFormData, TaskFormResult>(TaskFormDialog, {
        data: { task, members: this.members(), labels: this.labels(), tasks: this.allTasks(), readOnly: !this.canEdit(), save, refresh },
        width: '600px',
        maxWidth: 'calc(100vw - 32px)',
      })
      .afterClosed();

    if (task) {
      closed
        .pipe(
          filter((result) => result === 'deleteRequested'),
          switchMap(() =>
            confirm(this.dialog, {
              title: 'tasks.deleteTitle',
              message: 'tasks.deleteMessage',
              confirm: 'tasks.delete',
              params: { title: task.title },
            }),
          ),
          filter(Boolean),
        )
        .subscribe(() => this.store.dispatch(TasksPageActions.deleteConfirmed({ taskId: task.id })));
    }
  }
}
