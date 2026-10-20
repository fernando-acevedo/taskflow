import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { Task } from '../data-access/tasks-api.service';
import { TasksPageActions } from '../data-access/store/tasks.actions';
import { TaskFormData, TaskFormDialog, TaskFormResult } from './task-form-dialog';

/** Opens a task (or a new one) in the form dialog and dispatches what the user chose. Shared by the board and the list. */
@Injectable({ providedIn: 'root' })
export class TaskEditor {
  private readonly store = inject(Store);
  private readonly dialog = inject(MatDialog);
  private readonly members = this.store.selectSignal(projectsFeature.selectMembers);
  private readonly canEdit = this.store.selectSignal(projectsFeature.selectCanEditTasks);

  open(task: Task | null = null): void {
    this.dialog
      .open<TaskFormDialog, TaskFormData, TaskFormResult>(TaskFormDialog, {
        data: { task, members: this.members(), readOnly: !this.canEdit() },
        width: '600px',
        maxWidth: 'calc(100vw - 32px)',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result?.action === 'save') {
          this.store.dispatch(
            task
              ? TasksPageActions.updateSubmitted({ taskId: task.id, changes: result.task })
              : TasksPageActions.createSubmitted({ task: result.task }),
          );
        } else if (result?.action === 'delete' && task) {
          this.confirmDelete(task);
        }
      });
  }

  private confirmDelete(task: Task): void {
    confirm(this.dialog, {
      title: 'tasks.deleteTitle',
      message: 'tasks.deleteMessage',
      confirm: 'tasks.delete',
      params: { title: task.title },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(TasksPageActions.deleteConfirmed({ taskId: task.id })));
  }
}
