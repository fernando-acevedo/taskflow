import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { Notifier } from '@core/notifications/notifier';
import { RealtimeService } from '@core/realtime/realtime.service';
import { EmptyState } from '@shared/ui/empty-state';
import { LabelsPageActions } from '@features/labels';
import { TasksPageActions } from '@features/tasks';
import { ProjectPageActions } from '../data-access/store/projects.actions';
import { projectsFeature } from '../data-access/store/projects.feature';

/** The shell of an open project: its header and tabs. It loads the project, its members and its tasks. */
@Component({
  selector: 'app-project-page',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTabsModule,
    TranslocoDirective,
    EmptyState,
  ],
  templateUrl: './project-page.html',
  styleUrl: './project-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectPage {
  private readonly store = inject(Store);
  protected readonly notifier = inject(Notifier);
  private readonly realtime = inject(RealtimeService);
  /** "Live" or "Reconnecting" next to the project's title. */
  protected readonly realtimeState = this.realtime.state;
  /** What the live region says: only a connection that drops or comes back, never the "Live" shown on every load. */
  protected readonly realtimeAnnouncement = signal<'reconnecting' | 'restored' | null>(null);

  /** Bound from the route. */
  readonly projectId = input.required<string>();

  protected readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly loading = this.store.selectSignal(projectsFeature.selectCurrentLoading);
  protected readonly error = this.store.selectSignal(projectsFeature.selectCurrentError);
  protected readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);

  protected readonly tabs = [
    { path: 'board', label: 'project.tabs.board', icon: 'view_kanban' },
    { path: 'list', label: 'project.tabs.list', icon: 'list' },
    { path: 'overview', label: 'project.tabs.overview', icon: 'insights' },
    { path: 'hours', label: 'project.tabs.hours', icon: 'schedule' },
    { path: 'activity', label: 'project.tabs.activity', icon: 'history' },
    { path: 'members', label: 'project.tabs.members', icon: 'group' },
    { path: 'settings', label: 'project.tabs.settings', icon: 'settings' },
  ];

  constructor() {
    let wasLive = false;
    effect(() => {
      const state = this.realtimeState();
      if (state === 'reconnecting' || state === 'disconnected') {
        this.realtimeAnnouncement.set('reconnecting');
      } else if (state === 'live') {
        this.realtimeAnnouncement.set(untracked(this.realtimeAnnouncement) === 'reconnecting' && !wasLive ? 'restored' : null);
      }
      wasLive = state === 'live';
    });
    effect(() => {
      const projectId = this.projectId();
      this.store.dispatch(ProjectPageActions.opened({ projectId }));
      this.store.dispatch(TasksPageActions.opened({ projectId }));
      this.store.dispatch(LabelsPageActions.opened({ projectId }));
    });
    // Watch the open project on the hub (ADR 0023) for as long as the page shows it.
    effect((onCleanup) => {
      const projectId = this.projectId();
      this.realtime.join(projectId);
      onCleanup(() => this.realtime.leave(projectId));
    });
  }

  protected reload(): void {
    this.store.dispatch(ProjectPageActions.opened({ projectId: this.projectId() }));
    this.store.dispatch(TasksPageActions.opened({ projectId: this.projectId() }));
    this.store.dispatch(LabelsPageActions.opened({ projectId: this.projectId() }));
  }
}
