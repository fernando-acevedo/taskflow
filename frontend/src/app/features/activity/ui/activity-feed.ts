import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { relativeTime } from '../../../shared/ui/relative-time';
import { ActivityEntry } from '../data-access/activity-api.service';
import { describeActivity } from './activity-text';

/**
 * A list of activity entries, newest first, with a "Load more" button. A task's title is a button that
 * opens the task when its id is in `linkableTaskIds` (the task still exists); otherwise it's plain text.
 */
@Component({
  selector: 'app-activity-feed',
  imports: [DatePipe, MatButtonModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      @if (error()) {
        <p class="note">{{ t('activity.loadFailed') }}</p>
      } @else if (loading() && items().length === 0) {
        <p class="note">{{ t('activity.loading') }}</p>
      } @else if (items().length === 0) {
        <p class="note empty">{{ t('activity.empty') }}</p>
      }

      <ul class="entries">
        @for (entry of items(); track entry.id) {
          @let view = describe(entry, t);
          <li class="entry">
            <p class="sentence">
              @if (view.aroundTask && entry.taskId && linkableTaskIds().has(entry.taskId)) {
                {{ view.aroundTask[0] }}<button type="button" class="task-link" (click)="openTask.emit(entry.taskId)">{{ taskTitle(entry) }}</button>{{ view.aroundTask[1] }}
              } @else {
                {{ view.text }}
              }
            </p>
            @if (view.details.length > 0) {
              <ul class="details">
                @for (line of view.details; track $index) {
                  <li>{{ line }}</li>
                }
              </ul>
            }
            @if (entry.viaApiKey) {
              <span class="via-key">{{ t('activity.viaApiKey') }}</span>
            }
            <time [attr.datetime]="entry.occurredAt" [title]="entry.occurredAt | date: 'medium' : undefined : language.current()">
              {{ when(entry) }}
            </time>
          </li>
        }
      </ul>

      @if (hasMore() && !error()) {
        <button mat-stroked-button type="button" class="more" [disabled]="loading()" (click)="loadMore.emit()">
          {{ t(loading() ? 'activity.loading' : 'activity.loadMore') }}
        </button>
      }
    </ng-container>
  `,
  styles: `
    .entries,
    .details {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    .entries {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin-bottom: 12px;
    }
    .sentence {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .details {
      margin-top: 2px;
      padding-left: 16px;
      font-size: 0.875rem;
      color: var(--mat-sys-on-surface-variant);
    }
    .via-key {
      font-size: 0.8125rem;
      color: var(--mat-sys-on-surface-variant);
      margin-right: 8px;
    }
    time,
    .note {
      font-size: 0.8125rem;
      color: var(--mat-sys-on-surface-variant);
    }
    .task-link {
      all: unset;
      cursor: pointer;
      font-weight: 500;
      color: var(--mat-sys-primary);
      text-decoration: underline;
    }
    .task-link:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityFeed {
  readonly items = input.required<readonly ActivityEntry[]>();
  readonly loading = input(false);
  readonly error = input<unknown>(null);
  readonly hasMore = input(false);
  /** Ids of the tasks that can still be opened. */
  readonly linkableTaskIds = input<ReadonlySet<string>>(new Set());

  readonly loadMore = output();
  readonly openTask = output<string>();

  protected readonly language = inject(LanguageService);
  private readonly now = signal(Date.now());

  constructor() {
    // "5 minutes ago" keeps moving while the feed stays open.
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected describe = describeActivity;

  protected taskTitle(entry: ActivityEntry): string {
    const title = entry.changes['taskTitle'];
    return typeof title === 'string' ? title : '';
  }

  protected when(entry: ActivityEntry): string {
    return relativeTime(entry.occurredAt, this.now(), this.language.current());
  }
}
