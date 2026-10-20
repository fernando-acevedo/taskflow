import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Store } from '@ngrx/store';
import { filter, switchMap } from 'rxjs';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { Task, TaskInput } from '../data-access/tasks-api.service';
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
  private readonly canEdit = this.store.selectSignal(projectsFeature.selectCanEditTasks);

  open(task: Task | null = null): void {
    const save: SaveFn<TaskInput> = (input) =>
      this.submissions.dispatchAndWait(
        task
          ? TasksPageActions.updateSubmitted({ taskId: task.id, changes: input, submissionId: nextSubmissionId() })
          : TasksPageActions.createSubmitted({ task: input, submissionId: nextSubmissionId() }),
      );

    const closed = this.dialog
      .open<TaskFormDialog, TaskFormData, TaskFormResult>(TaskFormDialog, {
        data: { task, members: this.members(), readOnly: !this.canEdit(), save },
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
