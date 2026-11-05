import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { map, of } from 'rxjs';
import { Notifier } from '../../../core/notifications/notifier';
import { EmptyState } from '../../../shared/ui/empty-state';
import { formatMinutes } from '../../../shared/ui/duration';
import { BarList, BarRow } from '../../dashboard/ui/bar-list';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { toTimeQueryParams } from '../data-access/store/time-filters';
import { TimeReportPageActions } from '../data-access/store/time-tracking.actions';
import { timeTrackingFeature } from '../data-access/store/time-tracking.feature';
import { NO_TIME_FILTERS, TimeFilters } from '../data-access/time-tracking-api.service';
import { localToday } from '../../../shared/ui/local-today';
import { HoursBar } from '../ui/hours-bar';

const MAX_EXPORT_DAYS = 366;

/** First and last day of the current month, `yyyy-MM-dd`. */
function currentMonth(today = localToday()): { from: string; to: string } {
  const [year, month] = today.split('-').map(Number);
  const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
  const pad = (n: number): string => String(n).padStart(2, '0');
  return { from: `${year}-${pad(month)}-01`, to: `${year}-${pad(month)}-${pad(last)}` };
}

/**
 * The project's hours tab: who logged time and on which tasks (estimate against actual), over a date range
 * and optionally for one person. The filters live in the URL query string, like the task list's. The CSV is
 * fetched through the app's HTTP client so the bearer token travels with the request.
 */
@Component({
  selector: 'app-time-report-page',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    TranslocoDirective,
    EmptyState,
    BarList,
    HoursBar,
  ],
  templateUrl: './time-report-page.html',
  styles: `
    :host {
      display: grid;
      gap: 16px;
    }
    .filters .range {
      flex: 0 1 180px;
    }
    .actions {
      display: flex;
      flex: 1 0 auto;
      justify-content: flex-end;
      gap: 8px;
    }
    .total {
      margin: 0;
      font: var(--mat-sys-title-large);
      font-variant-numeric: tabular-nums;
    }
    .none {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
    mat-card {
      overflow-x: auto;
    }
    table {
      width: 100%;
      min-width: 560px;
      border-collapse: collapse;
    }
    th,
    td {
      padding: 10px 16px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      text-align: left;
      vertical-align: top;
    }
    th {
      font-weight: 500;
    }
    .bar {
      min-width: 220px;
    }
    .num {
      text-align: right;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimeReportPage {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly notifier = inject(Notifier);
  protected readonly format = formatMinutes;

  protected readonly filters = this.store.selectSignal(timeTrackingFeature.selectFilters);
  protected readonly report = this.store.selectSignal(timeTrackingFeature.selectReport);
  protected readonly loading = this.store.selectSignal(timeTrackingFeature.selectReportLoading);
  protected readonly error = this.store.selectSignal(timeTrackingFeature.selectReportError);
  protected readonly exporting = this.store.selectSignal(timeTrackingFeature.selectExporting);
  protected readonly members = this.store.selectSignal(projectsFeature.selectMembers);

  protected readonly filtered = computed(() => {
    const { from, to, userId } = this.filters();
    return from !== null || to !== null || userId !== null;
  });
  /** A range that ends before it starts has no sensible answer; the API would refuse it. */
  protected readonly invalidRange = computed(() => {
    const { from, to } = this.filters();
    return from !== null && to !== null && from > to;
  });
  /** The CSV needs both dates, the start not after the end, and at most 366 days between them. */
  protected readonly exportRangeValid = computed(() => {
    const { from, to } = this.filters();
    if (!from || !to || from > to) {
      return false;
    }
    return (Date.parse(to) - Date.parse(from)) / 86_400_000 <= MAX_EXPORT_DAYS;
  });
  private readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);

  /** The project in the URL. */
  private readonly projectId = toSignal<string | null>(
    this.route.parent ? this.route.parent.paramMap.pipe(map((params) => params.get('projectId'))) : of(null),
    { initialValue: null },
  );

  protected readonly userRows = computed<BarRow[]>(() =>
    (this.report()?.byUser ?? []).map((row) => ({
      key: row.userId,
      label: row.userName,
      value: row.minutes,
      text: formatMinutes(row.minutes),
    })),
  );

  constructor() {
    // Only the owner sees the hours of everyone: anyone else goes back to the board.
    effect(() => {
      const projectId = this.projectId();
      if (projectId !== null && this.project() !== null && !this.isOwner()) {
        void this.router.navigate(['/projects', projectId, 'board'], { replaceUrl: true });
      }
    });

    // Suggest the current month when the URL has no range.
    const current = this.filters();
    if (current.from === null && current.to === null) {
      this.setFilter(currentMonth());
    }

    effect(() => {
      const projectId = this.projectId();
      const filters = this.filters();
      if (projectId !== null && this.isOwner() && !this.invalidRange()) {
        this.store.dispatch(TimeReportPageActions.opened({ projectId, filters }));
      }
    });
  }

  protected setFilter(changes: Partial<TimeFilters>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: toTimeQueryParams({ ...this.filters(), ...changes }),
      replaceUrl: true,
    });
  }

  protected clearFilters(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: toTimeQueryParams(NO_TIME_FILTERS), replaceUrl: true });
  }

  protected exportCsv(): void {
    const projectId = this.projectId();
    if (projectId !== null && this.exportRangeValid()) {
      this.store.dispatch(TimeReportPageActions.exportClicked({ projectId, filters: this.filters() }));
    }
  }

  protected setDate(field: 'from' | 'to', event: Event): void {
    this.setFilter({ [field]: (event.target as HTMLInputElement).value || null });
  }

  protected reload(): void {
    const projectId = this.projectId();
    if (projectId !== null) {
      this.store.dispatch(TimeReportPageActions.opened({ projectId, filters: this.filters() }));
    }
  }
}
