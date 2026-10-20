import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { LanguageService } from '../../../core/i18n/language.service';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { ProjectInput } from '../data-access/projects-api.service';
import { ProjectPageActions } from '../data-access/store/projects.actions';
import { projectsFeature } from '../data-access/store/projects.feature';
import { ProjectFormData, ProjectFormDialog } from '../ui/project-form-dialog';

/** Owner-only: edit the project's details, archive or restore it. */
@Component({
  selector: 'app-settings-page',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatIconModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'projects'">
      @if (project(); as current) {
        <mat-card appearance="outlined">
          <mat-card-content>
            <dl class="facts">
              <dt>{{ t('name') }}</dt>
              <dd>{{ current.name }}</dd>
              <dt>{{ t('client') }}</dt>
              <dd>{{ current.clientName }}</dd>
              <dt>{{ t('description') }}</dt>
              <dd>{{ current.description || '—' }}</dd>
              <dt>{{ t('created') }}</dt>
              <dd>{{ current.createdAt | date: 'mediumDate' : undefined : language.current() }}</dd>
            </dl>
          </mat-card-content>
          @if (isOwner()) {
            <mat-card-actions align="end">
              @if (current.isArchived) {
                <button mat-button type="button" (click)="restore()" [disabled]="saving()">
                  <mat-icon aria-hidden="true">unarchive</mat-icon>
                  {{ t('restore') }}
                </button>
              } @else {
                <button mat-button type="button" (click)="archive()" [disabled]="saving()">
                  <mat-icon aria-hidden="true">archive</mat-icon>
                  {{ t('archive') }}
                </button>
                <button mat-flat-button type="button" (click)="edit()" [disabled]="saving()">
                  <mat-icon aria-hidden="true">edit</mat-icon>
                  {{ t('edit') }}
                </button>
              }
            </mat-card-actions>
          }
        </mat-card>
      }
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
      max-width: 720px;
    }
    .facts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 8px 24px;
      margin: 8px 0;
    }
    .facts dt {
      color: var(--mat-sys-on-surface-variant);
    }
    .facts dd {
      margin: 0;
      overflow-wrap: anywhere;
      white-space: pre-line;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage {
  private readonly store = inject(Store);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);

  protected readonly project = this.store.selectSignal(projectsFeature.selectCurrent);
  protected readonly isOwner = this.store.selectSignal(projectsFeature.selectIsOwner);
  protected readonly saving = this.store.selectSignal(projectsFeature.selectSaving);
  private readonly clients = this.store.selectSignal(projectsFeature.selectClientOptions);

  protected edit(): void {
    this.dialog
      .open<ProjectFormDialog, ProjectFormData, ProjectInput>(ProjectFormDialog, {
        data: { project: this.project(), clients: this.clients() },
        width: '520px',
      })
      .afterClosed()
      .pipe(filter((project) => project !== undefined))
      .subscribe((project) => this.store.dispatch(ProjectPageActions.updateSubmitted({ project })));
  }

  protected archive(): void {
    confirm(this.dialog, {
      title: 'projects.archiveTitle',
      message: 'projects.archiveMessage',
      confirm: 'projects.archive',
      params: { name: this.project()?.name },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(ProjectPageActions.archiveConfirmed()));
  }

  protected restore(): void {
    this.store.dispatch(ProjectPageActions.restoreClicked());
  }
}
