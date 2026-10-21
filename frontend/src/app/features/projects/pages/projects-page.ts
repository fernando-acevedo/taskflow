import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { LanguageService } from '../../../core/i18n/language.service';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { EmptyState } from '../../../shared/ui/empty-state';
import { PagedList } from '../../../shared/ui/paged-list';
import { SearchField } from '../../../shared/ui/search-field';
import { ProjectInput } from '../data-access/projects-api.service';
import { ProjectsPageActions } from '../data-access/store/projects.actions';
import { projectsFeature } from '../data-access/store/projects.feature';
import { ProjectFormData, ProjectFormDialog } from '../ui/project-form-dialog';

@Component({
  selector: 'app-projects-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslocoDirective,
    EmptyState,
    PagedList,
    SearchField,
  ],
  templateUrl: './projects-page.html',
  styles: `
    :host {
      display: block;
      max-width: 960px;
      margin: 0 auto;
      padding: 32px 16px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectsPage {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);

  protected readonly projects = this.store.selectSignal(projectsFeature.selectAll);
  protected readonly query = this.store.selectSignal(projectsFeature.selectQuery);
  protected readonly totalCount = this.store.selectSignal(projectsFeature.selectTotalCount);
  protected readonly loading = this.store.selectSignal(projectsFeature.selectLoading);
  protected readonly saving = this.store.selectSignal(projectsFeature.selectSaving);
  protected readonly loadError = this.store.selectSignal(projectsFeature.selectLoadError);
  protected readonly clients = this.store.selectSignal(projectsFeature.selectClientOptions);

  constructor() {
    this.store.dispatch(ProjectsPageActions.opened());
  }

  protected search(search: string): void {
    this.store.dispatch(ProjectsPageActions.queryChanged({ query: { search } }));
  }

  protected retry(): void {
    this.store.dispatch(ProjectsPageActions.opened());
  }

  protected filterByClient(clientId: string | null): void {
    this.store.dispatch(ProjectsPageActions.queryChanged({ query: { clientId } }));
  }

  protected showArchived(archived: boolean): void {
    this.store.dispatch(ProjectsPageActions.queryChanged({ query: { archived } }));
  }

  protected changePage(event: PageEvent): void {
    this.store.dispatch(ProjectsPageActions.queryChanged({ query: { page: event.pageIndex + 1, pageSize: event.pageSize } }));
  }

  /** On success the dialog closes and the new project opens (see openCreatedProject). */
  protected create(): void {
    const save: SaveFn<ProjectInput> = (project) =>
      this.submissions.dispatchAndWait(ProjectsPageActions.createSubmitted({ project, submissionId: nextSubmissionId() }));
    this.dialog.open<ProjectFormDialog, ProjectFormData>(ProjectFormDialog, {
      data: { project: null, clients: this.clients(), save },
      width: '520px',
    });
  }
}
