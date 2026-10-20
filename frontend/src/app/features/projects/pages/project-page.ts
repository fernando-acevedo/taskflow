import { ChangeDetectionStrategy, Component, effect, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { Notifier } from '../../../core/notifications/notifier';
import { EmptyState } from '../../../shared/ui/empty-state';
import { TasksPageActions } from '../../tasks/data-access/store/tasks.actions';
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

  /** Bound from the route. */
  readonly projectId = input.required<string>();

  protected readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly loading = this.store.selectSignal(projectsFeature.selectCurrentLoading);
  protected readonly error = this.store.selectSignal(projectsFeature.selectCurrentError);
  protected readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);

  protected readonly tabs = [
    { path: 'board', label: 'project.tabs.board', icon: 'view_kanban' },
    { path: 'list', label: 'project.tabs.list', icon: 'list' },
    { path: 'members', label: 'project.tabs.members', icon: 'group' },
    { path: 'settings', label: 'project.tabs.settings', icon: 'settings' },
  ];

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      this.store.dispatch(ProjectPageActions.opened({ projectId }));
      this.store.dispatch(TasksPageActions.opened({ projectId }));
    });
  }

  protected reload(): void {
    this.store.dispatch(ProjectPageActions.opened({ projectId: this.projectId() }));
    this.store.dispatch(TasksPageActions.opened({ projectId: this.projectId() }));
  }
}
