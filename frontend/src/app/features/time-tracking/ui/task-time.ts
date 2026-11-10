import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { authFeature } from '@core/auth/store/auth.feature';
import { LanguageService } from '@core/i18n/language.service';
import { SaveFn, Submissions, nextSubmissionId } from '@shared/data-access/submission';
import { confirm } from '@shared/ui/confirm-dialog';
import { formatMinutes } from '@shared/ui/duration';
import { projectsFeature } from '@features/projects';
import { TimeEntriesPageActions } from '../data-access/store/time-tracking.actions';
import { timeTrackingFeature } from '../data-access/store/time-tracking.feature';
import { TimeEntry, TimeEntryInput } from '../data-access/time-tracking-api.service';
import { HoursBar } from './hours-bar';
import { TimeEntryForm } from './time-entry-form';

interface EntryView {
  readonly entry: TimeEntry;
  readonly canChange: boolean;
  readonly save: SaveFn<TimeEntryInput>;
}

/**
 * The hours of a task: logged against the estimate, the entries newest first, and a small form to log more.
 * An entry is edited or deleted by whoever logged it, or by the project's owner (not by an admin who isn't
 * the owner). An archived project, or a user who isn't a member, only reads.
 */
@Component({
  selector: 'app-task-time',
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslocoDirective, HoursBar, TimeEntryForm],
  template: `
    <section class="time" *transloco="let t; prefix: 'time'" [attr.aria-label]="t('title')">
      <h3>{{ t('title') }}</h3>

      @if (loadError()) {
        <p class="note">{{ t('loadFailed') }}</p>
      } @else {
        <app-hours-bar [actual]="total()" [estimated]="estimated()" />
        @if (!isOwner()) {
          <p class="note">{{ t('yourEntries') }}</p>
        }

        @if (loading() && views().length === 0) {
          <p class="note">{{ t('loading') }}</p>
        } @else if (views().length === 0) {
          <p class="note empty">{{ t('empty') }}</p>
        }
      }

      <ul class="entries">
        @for (view of views(); track view.entry.id) {
          <li class="entry">
            <div class="meta">
              <strong class="user">{{ view.entry.userName }}</strong>
              <time class="date" [attr.datetime]="view.entry.date">
                {{ view.entry.date | date: 'd MMM yyyy' : 'UTC' : language.current() }}
              </time>
              <span class="minutes">{{ format(view.entry.minutes) }}</span>
              <span class="spacer"></span>
              @if (view.canChange && editingId() !== view.entry.id) {
                <button mat-icon-button type="button" [attr.aria-label]="t('edit')" (click)="editingId.set(view.entry.id)">
                  <mat-icon aria-hidden="true">edit</mat-icon>
                </button>
                <button mat-icon-button type="button" [attr.aria-label]="t('delete')" (click)="remove(view.entry)">
                  <mat-icon aria-hidden="true">delete</mat-icon>
                </button>
              }
            </div>
            @if (editingId() === view.entry.id) {
              <app-time-entry-form
                [save]="view.save"
                [entry]="view.entry"
                submitLabel="save"
                [cancellable]="true"
                (saved)="editingId.set(null)"
                (cancelled)="editingId.set(null)"
              />
            } @else if (view.entry.note) {
              <p class="entry-note">{{ view.entry.note }}</p>
            }
          </li>
        }
      </ul>

      @if (canLog()) {
        <app-time-entry-form [save]="saveNew" />
      } @else {
        <p class="note">{{ t('readOnly') }}</p>
      }
    </section>
  `,
  styles: `
    .time {
      display: block;
      margin-top: 16px;
    }
    h3 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-medium);
    }
    .entries {
      list-style: none;
      margin: 8px 0 12px;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .meta {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 40px;
      font-size: 0.875rem;
    }
    .date,
    .note {
      color: var(--mat-sys-on-surface-variant);
    }
    .minutes {
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }
    .spacer {
      flex: 1;
    }
    .entry-note {
      margin: 0;
      overflow-wrap: anywhere;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskTime implements OnInit {
  readonly projectId = input.required<string>();
  readonly taskId = input.required<string>();

  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);
  protected readonly format = formatMinutes;

  private readonly entries = this.store.selectSignal(timeTrackingFeature.selectEntries);
  protected readonly total = this.store.selectSignal(timeTrackingFeature.selectTotalMinutes);
  protected readonly estimated = this.store.selectSignal(timeTrackingFeature.selectEstimatedMinutes);
  protected readonly loading = this.store.selectSignal(timeTrackingFeature.selectLoading);
  protected readonly loadError = this.store.selectSignal(timeTrackingFeature.selectLoadError);
  private readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);
  private readonly user = this.store.selectSignal(authFeature.selectUser);
  protected readonly canLog = this.store.selectSignal(projectsFeature.selectCanEditTasks);

  protected readonly editingId = signal<string | null>(null);

  protected readonly views = computed<EntryView[]>(() => {
    const me = this.user()?.id;
    const archived = this.project()?.isArchived ?? false;
    const canManage = this.isOwner() && !archived;
    return this.entries().map((entry) => ({
      entry,
      canChange: (entry.userId === me && this.canLog()) || canManage,
      save: this.saveFor(entry.id),
    }));
  });

  protected readonly saveNew: SaveFn<TimeEntryInput> = (entry) =>
    this.submissions.dispatchAndWait(TimeEntriesPageActions.createSubmitted({ entry, submissionId: nextSubmissionId() }));

  ngOnInit(): void {
    this.store.dispatch(TimeEntriesPageActions.opened({ projectId: this.projectId(), taskId: this.taskId() }));
  }

  private saveFor(entryId: string): SaveFn<TimeEntryInput> {
    return (changes) =>
      this.submissions.dispatchAndWait(
        TimeEntriesPageActions.updateSubmitted({ entryId, changes, submissionId: nextSubmissionId() }),
      );
  }

  protected remove(entry: TimeEntry): void {
    confirm(this.dialog, {
      title: 'time.deleteTitle',
      message: 'time.deleteMessage',
      confirm: 'time.delete',
      params: { time: formatMinutes(entry.minutes), user: entry.userName },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(TimeEntriesPageActions.deleteConfirmed({ entryId: entry.id })));
  }
}
