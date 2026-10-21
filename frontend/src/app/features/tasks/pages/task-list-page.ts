import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { LanguageService } from '../../../core/i18n/language.service';
import { EmptyState } from '../../../shared/ui/empty-state';
import { SearchField } from '../../../shared/ui/search-field';
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
  protected readonly members = this.store.selectSignal(projectsFeature.selectMembers);

  protected readonly filtered = computed(() => hasActiveFilters(this.filters()));
  private readonly memberNames = this.store.selectSignal(projectsFeature.selectMemberNames);

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
