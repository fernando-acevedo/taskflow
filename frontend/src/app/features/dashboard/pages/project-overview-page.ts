import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { LanguageService } from '../../../core/i18n/language.service';
import { Notifier } from '../../../core/notifications/notifier';
import { EmptyState } from '../../../shared/ui/empty-state';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { TaskStatus } from '../../tasks/data-access/tasks-api.service';
import { ProjectOverviewActions } from '../data-access/store/dashboard.actions';
import { projectDashboardFeature } from '../data-access/store/project-dashboard.feature';
import { BarList, BarRow } from '../ui/bar-list';
import { StatTile } from '../ui/stat-tile';
import { StatusBar, StatusSegment } from '../ui/status-bar';

const STATUS_COLORS: Record<TaskStatus, string> = {
  ToDo: 'var(--tf-chart-todo)',
  InProgress: 'var(--tf-chart-in-progress)',
  Review: 'var(--tf-chart-review)',
  Done: 'var(--tf-chart-done)',
};

/** The project's metrics tab: where the work stands, who carries it and how urgent it is. */
@Component({
  selector: 'app-project-overview-page',
  imports: [MatButtonModule, MatCardModule, MatProgressBarModule, TranslocoDirective, EmptyState, BarList, StatTile, StatusBar],
  templateUrl: './project-overview-page.html',
  styles: `
    :host {
      display: grid;
      gap: 16px;
    }
    .kpis {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 12px;
    }
    .charts {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 380px), 1fr));
      gap: 16px;
      align-items: start;
    }
    mat-card-content {
      padding-top: 8px;
    }
    .none {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectOverviewPage {
  private readonly store = inject(Store);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);
  protected readonly notifier = inject(Notifier);

  private readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  /** Only the id: reloads of the project itself must not refetch the metrics. */
  private readonly projectId = computed(() => this.project()?.id);
  protected readonly dashboard = this.store.selectSignal(projectDashboardFeature.selectDashboard);
  protected readonly loading = this.store.selectSignal(projectDashboardFeature.selectLoading);
  protected readonly error = this.store.selectSignal(projectDashboardFeature.selectError);

  protected readonly doneCount = computed(
    () => this.dashboard()?.byStatus.find((row) => row.status === 'Done')?.count ?? 0,
  );

  protected readonly statusSegments = computed<StatusSegment[]>(() => {
    this.language.current();
    return (this.dashboard()?.byStatus ?? []).map(({ status, count }) => ({
      key: status,
      label: this.transloco.translate(`tasks.statuses.${status}`),
      count,
      color: STATUS_COLORS[status],
    }));
  });

  protected readonly assigneeRows = computed<BarRow[]>(() => {
    this.language.current();
    return (this.dashboard()?.openByAssignee ?? []).map((load) => ({
      key: load.userId ?? 'none',
      label:
        load.displayName ?? this.transloco.translate(load.userId ? 'tasks.formerMember' : 'tasks.unassigned'),
      value: load.openTasks,
      note: load.overdueTasks ? this.transloco.translate('dashboard.overdueCount', { count: load.overdueTasks }) : undefined,
      alert: load.overdueTasks > 0,
    }));
  });

  /** Most urgent first. */
  protected readonly priorityRows = computed<BarRow[]>(() => {
    this.language.current();
    return [...(this.dashboard()?.openByPriority ?? [])].reverse().map(({ priority, count }) => ({
      key: priority,
      label: this.transloco.translate(`tasks.priorities.${priority}`),
      value: count,
    }));
  });

  constructor() {
    effect(() => this.load());
  }

  protected load(): void {
    const projectId = this.projectId();
    if (projectId) {
      this.store.dispatch(ProjectOverviewActions.opened({ projectId }));
    }
  }
}
