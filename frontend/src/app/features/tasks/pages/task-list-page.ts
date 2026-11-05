import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { map, of } from 'rxjs';
import { LanguageService } from '../../../core/i18n/language.service';
import { EmptyState } from '../../../shared/ui/empty-state';
import { SearchField } from '../../../shared/ui/search-field';
import { labelsFeature } from '../../labels/data-access/store/labels.feature';
import { LabelChip } from '../../labels/ui/label-chip';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { TASK_PRIORITIES, TASK_STATUSES, Task } from '../data-access/tasks-api.service';
import { TaskFilters, hasActiveFilters, toQueryParams } from '../data-access/store/task-filters';
import { tasksFeature } from '../data-access/store/tasks.feature';
import { TaskEditor } from '../ui/task-editor';

/** All tasks of the project in a list, filtered by the URL query string (`?status=Review&assignee=me`). */
@Component({
  selector: 'app-task-list-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslocoDirective,
    EmptyState,
    SearchField,
    LabelChip,
  ],
  templateUrl: './task-list-page.html',
  styleUrl: './task-list-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TaskListPage {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly editor = inject(TaskEditor);
  protected readonly language = inject(LanguageService);

  protected readonly statuses = TASK_STATUSES;
  protected readonly priorities = TASK_PRIORITIES;
  protected readonly filters = this.store.selectSignal(tasksFeature.selectFilters);
  protected readonly tasks = this.store.selectSignal(tasksFeature.selectFilteredTasks);
  protected readonly total = this.store.selectSignal(tasksFeature.selectTotal);
  protected readonly loading = this.store.selectSignal(tasksFeature.selectLoading);
  protected readonly canEdit = this.store.selectSignal(projectsFeature.selectCanEditTasks);
  protected readonly labels = this.store.selectSignal(labelsFeature.selectAll);
  protected readonly members = this.store.selectSignal(projectsFeature.selectMembers);

  protected readonly filtered = computed(() => hasActiveFilters(this.filters()));
  private readonly memberNames = this.store.selectSignal(projectsFeature.selectMemberNames);

  private readonly labelsReady = this.store.selectSignal(labelsFeature.selectLoading);
  private readonly labelsProject = this.store.selectSignal(labelsFeature.selectProjectId);
  private readonly labelsError = this.store.selectSignal(labelsFeature.selectLoadError);

  /** The project in the URL; the labels in the store may still belong to the previous one. */
  private readonly projectId = toSignal<string | null>(
    this.route.parent ? this.route.parent.paramMap.pipe(map((params) => params.get('projectId'))) : of(null),
    { initialValue: null },
  );

  constructor() {
    // A label that was deleted (or never existed) would leave the list empty with no explanation.
    effect(() => {
      const selected = this.filters().label;
      const projectId = this.projectId();
      const loaded = projectId !== null && this.labelsProject() === projectId && !this.labelsReady() && !this.labelsError();
      if (selected && loaded && !this.labels().some((label) => label.id === selected)) {
        this.setFilter({ label: null });
      }
    });
  }

  protected assigneeName(task: Task): string | null {
    return task.assigneeId ? (this.memberNames().get(task.assigneeId) ?? null) : null;
  }

  protected setFilter(changes: Partial<TaskFilters>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: toQueryParams({ ...this.filters(), ...changes }),
      replaceUrl: true,
    });
  }

  protected clearFilters(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }
}
