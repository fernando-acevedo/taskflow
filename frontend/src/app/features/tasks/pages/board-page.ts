import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { Notifier } from '../../../core/notifications/notifier';
import { EmptyState } from '../../../shared/ui/empty-state';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { TASK_STATUSES, Task, TaskStatus } from '../data-access/tasks-api.service';
import { TasksPageActions } from '../data-access/store/tasks.actions';
import { tasksFeature } from '../data-access/store/tasks.feature';
import { TaskCard } from '../ui/task-card';
import { TaskEditor } from '../ui/task-editor';

/**
 * The Kanban board. A drop moves the card at once (optimistic update in the reducer) and the API call follows;
 * if it fails, the card goes back and a message explains why. Dragging pauses while a move is in flight.
 */
@Component({
  selector: 'app-board-page',
  imports: [CdkDrag, CdkDropList, CdkDropListGroup, MatButtonModule, MatIconModule, TranslocoDirective, EmptyState, TaskCard],
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoardPage {
  private readonly store = inject(Store);
  protected readonly editor = inject(TaskEditor);
  protected readonly notifier = inject(Notifier);

  protected readonly statuses = TASK_STATUSES;
  protected readonly columns = this.store.selectSignal(tasksFeature.selectColumns);
  protected readonly loading = this.store.selectSignal(tasksFeature.selectLoading);
  protected readonly loadError = this.store.selectSignal(tasksFeature.selectLoadError);
  protected readonly moving = this.store.selectSignal(tasksFeature.selectMoving);
  protected readonly canEdit = this.store.selectSignal(projectsFeature.selectCanEditTasks);
  private readonly projectId = this.store.selectSignal(tasksFeature.selectProjectId);
  private readonly members = this.store.selectSignal(projectsFeature.selectMembers);

  private readonly memberNames = computed(
    () => new Map(this.members().map((member) => [member.userId, member.displayName] as const)),
  );

  protected assigneeName(task: Task): string | null {
    return task.assigneeId ? (this.memberNames().get(task.assigneeId) ?? null) : null;
  }

  protected drop(event: CdkDragDrop<TaskStatus, TaskStatus, Task>): void {
    const status = event.container.data;
    if (event.previousContainer === event.container && event.previousIndex === event.currentIndex) {
      return;
    }
    this.store.dispatch(TasksPageActions.taskDropped({ taskId: event.item.data.id, status, position: event.currentIndex }));
  }

  protected retry(): void {
    const projectId = this.projectId();
    if (projectId) {
      this.store.dispatch(TasksPageActions.opened({ projectId }));
    }
  }
}
