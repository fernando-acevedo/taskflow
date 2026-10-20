import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { Task } from '../data-access/tasks-api.service';

/** A task as shown on the board: title, priority, assignee and due date. Presentational only. */
@Component({
  selector: 'app-task-card',
  imports: [DatePipe, MatIconModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'tasks'">
      <span class="title">{{ task().title }}</span>
      <span class="details">
        <span class="priority" [attr.data-priority]="task().priority">{{ t('priorities.' + task().priority) }}</span>
        @if (task().dueDate; as due) {
          <span class="due" [class.overdue]="task().isOverdue">
            <mat-icon aria-hidden="true">{{ task().isOverdue ? 'warning' : 'event' }}</mat-icon>
            {{ due | date: 'd MMM' : 'UTC' : language.current() }}
            @if (task().isOverdue) {
              <span class="visually-hidden">{{ t('overdue') }}</span>
            }
          </span>
        }
        <span class="assignee">{{ assignee() ?? t('unassigned') }}</span>
      </span>
    </ng-container>
  `,
  styleUrl: './task-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskCard {
  protected readonly language = inject(LanguageService);

  readonly task = input.required<Task>();
  /** The assignee's display name, or null when unassigned. */
  readonly assignee = input<string | null>(null);
}
