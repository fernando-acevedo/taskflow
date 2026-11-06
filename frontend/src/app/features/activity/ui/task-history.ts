import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { ActivityPageActions } from '../data-access/store/activity.actions';
import { activityFeature } from '../data-access/store/activity.feature';
import { ActivityFeed } from './activity-feed';

/** The history of one task, collapsed until opened so the dialog stays short and the request waits for the click. */
@Component({
  selector: 'app-task-history',
  imports: [TranslocoDirective, ActivityFeed],
  template: `
    <details class="history" *transloco="let t" (toggle)="toggled($event)">
      <summary>{{ t('activity.history') }}</summary>
      @if (opened) {
        <app-activity-feed
          [items]="feed().items"
          [loading]="feed().loading"
          [error]="feed().error"
          [hasMore]="hasMore()"
          (loadMore)="loadMore()"
        />
      }
    </details>
  `,
  styles: `
    .history {
      margin-top: 16px;
    }
    summary {
      cursor: pointer;
      font: var(--mat-sys-title-medium);
      margin-bottom: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskHistory {
  readonly projectId = input.required<string>();
  readonly taskId = input.required<string>();

  private readonly store = inject(Store);
  protected readonly feed = this.store.selectSignal(activityFeature.selectTask);
  protected readonly hasMore = this.store.selectSignal(activityFeature.selectTaskHasMore);
  protected opened = false;

  protected toggled(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open && !this.opened) {
      this.opened = true;
      this.store.dispatch(ActivityPageActions.opened({ scope: 'task', projectId: this.projectId(), taskId: this.taskId() }));
    }
  }

  protected loadMore(): void {
    this.store.dispatch(ActivityPageActions.loadMoreClicked({ scope: 'task' }));
  }
}
