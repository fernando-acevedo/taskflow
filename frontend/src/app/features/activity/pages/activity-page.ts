import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { tasksFeature } from '../../tasks/data-access/store/tasks.feature';
import { TaskEditor } from '../../tasks/ui/task-editor';
import { ActivityPageActions } from '../data-access/store/activity.actions';
import { activityFeature } from '../data-access/store/activity.feature';
import { ActivityFeed } from '../ui/activity-feed';

/** The project's activity tab: what happened in it, newest first. */
@Component({
  selector: 'app-activity-page',
  imports: [TranslocoDirective, ActivityFeed],
  template: `
    <section *transloco="let t" [attr.aria-label]="t('activity.title')">
      <h2 class="title">{{ t('activity.title') }}</h2>
      <app-activity-feed
        [items]="feed().items"
        [loading]="feed().loading"
        [error]="feed().error"
        [hasMore]="hasMore()"
        [linkableTaskIds]="existingTaskIds()"
        (loadMore)="loadMore()"
        (openTask)="open($event)"
      />
    </section>
  `,
  styles: `
    .title {
      margin: 16px 0 8px;
      font: var(--mat-sys-title-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityPage {
  private readonly store = inject(Store);
  private readonly editor = inject(TaskEditor);

  private readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly feed = this.store.selectSignal(activityFeature.selectProject);
  protected readonly hasMore = this.store.selectSignal(activityFeature.selectProjectHasMore);
  private readonly tasks = this.store.selectSignal(tasksFeature.selectAll);
  protected readonly existingTaskIds = computed(() => new Set(this.tasks().map((task) => task.id)));

  private readonly projectId = computed(() => this.project()?.id);

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      if (projectId) {
        this.store.dispatch(ActivityPageActions.opened({ scope: 'project', projectId, taskId: null }));
      }
    });
  }

  protected loadMore(): void {
    this.store.dispatch(ActivityPageActions.loadMoreClicked({ scope: 'project' }));
  }

  protected open(taskId: string): void {
    const task = this.tasks().find((candidate) => candidate.id === taskId);
    if (task) {
      this.editor.open(task);
    }
  }
}
