import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '../../../core/i18n/language.service';
import { LabelChip } from '../../labels/ui/label-chip';
import { Task } from '../data-access/tasks-api.service';

/** A task as shown on the board: title, priority, assignee and due date. Presentational only. */
@Component({
  selector: 'app-task-card',
  imports: [DatePipe, MatIconModule, TranslocoDirective, LabelChip],
  template: `
    <ng-container *transloco="let t; prefix: 'tasks'">
      <span class="title">{{ task().title }}</span>
      @if (task().labels.length > 0) {
        <span class="labels">
          @for (label of task().labels; track label.id) {
            <app-label-chip [label]="label" />
          }
        </span>
      }
      @if (task().isBlocked) {
        <span class="blocked">
          <mat-icon aria-hidden="true">block</mat-icon>
          {{ t('blocked') }}
        </span>
      }
      @if (task().statusNote; as note) {
        <span class="note">{{ note }}</span>
      }
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
