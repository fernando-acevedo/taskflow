import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { map, of } from 'rxjs';
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
 * if it fails, the card goes back and a message explains why. Dragging pauses while a move is in flight. Each
 * card also has a "Move to" menu that does the same without a mouse.
 */
@Component({
  selector: 'app-board-page',
  imports: [
    CdkDrag,
    CdkDropList,
    CdkDropListGroup,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    TranslocoDirective,
    EmptyState,
    TaskCard,
  ],
  templateUrl: './board-page.html',
  styleUrl: './board-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoardPage {
  private readonly store = inject(Store);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly editor = inject(TaskEditor);
  protected readonly notifier = inject(Notifier);

  protected readonly statuses = TASK_STATUSES;
  protected readonly columns = this.store.selectSignal(tasksFeature.selectColumns);
  protected readonly loading = this.store.selectSignal(tasksFeature.selectLoading);
  protected readonly loadError = this.store.selectSignal(tasksFeature.selectLoadError);
  protected readonly moving = this.store.selectSignal(tasksFeature.selectMoving);
  protected readonly canEdit = this.store.selectSignal(projectsFeature.selectCanEditTasks);
  private readonly projectId = this.store.selectSignal(tasksFeature.selectProjectId);
  private readonly memberNames = this.store.selectSignal(projectsFeature.selectMemberNames);

  /** The task a notification (or an emailed link) points at: `?task={id}`. */
  private readonly linkedTaskId = toSignal<string | null>(this.route.queryParamMap.pipe(map((params) => params.get('task'))), {
    initialValue: null,
  });

  /** The project in the URL; the tasks in the store may still belong to the previous one. */
  private readonly routeProjectId = toSignal<string | null>(
    this.route.parent ? this.route.parent.paramMap.pipe(map((params) => params.get('projectId'))) : of(null),
    { initialValue: null },
  );

  private handledTaskId: string | null = null;

  constructor() {
    // Opens the linked task once this project's tasks are in, then drops the parameter so a reload or a
    // later move does not open it again.
    effect(() => {
      const taskId = this.linkedTaskId();
      const loaded =
        this.routeProjectId() !== null &&
        this.projectId() === this.routeProjectId() &&
        !this.loading() &&
        this.loadError() === null;
      if (!taskId) {
        // The parameter is gone (we removed it): the same link can open its task again.
        this.handledTaskId = null;
        return;
      }
      if (!loaded || taskId === this.handledTaskId) {
        return;
      }
      this.handledTaskId = taskId;
      const task = Object.values(this.columns()).flat().find((candidate) => candidate.id === taskId);
      untracked(() => {
        if (task) {
          this.editor.open(task);
        } else {
          this.notifier.error({ code: 'Tasks.NotFound', fields: {} });
        }
        void this.router.navigate([], { relativeTo: this.route, queryParams: { task: null }, queryParamsHandling: 'merge', replaceUrl: true });
      });
    });
  }

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

  /** From the card's menu: to the end of another column. */
  protected moveTo(task: Task, status: TaskStatus): void {
    const position = this.columns()[status].length;
    this.store.dispatch(TasksPageActions.taskDropped({ taskId: task.id, status, position }));
  }

  /** From the card's menu: one place up or down in its own column. */
  protected moveWithin(task: Task, position: number): void {
    this.store.dispatch(TasksPageActions.taskDropped({ taskId: task.id, status: task.status, position }));
  }

  protected retry(): void {
    const projectId = this.projectId();
    if (projectId) {
      this.store.dispatch(TasksPageActions.opened({ projectId }));
    }
  }
}
