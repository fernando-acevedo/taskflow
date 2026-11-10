import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Observable } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import { applyApiFieldErrors } from '../../../shared/data-access/api-field-errors';
import { isStale } from '../../../core/http/api-error';
import { StaleNotice } from '../../../shared/ui/stale-notice';
import { SaveFn } from '../../../shared/data-access/submission';
import { ClientOption, Project, ProjectInput } from '../data-access/projects-api.service';

export interface ProjectFormData {
  readonly project: Project | null;
  readonly clients: readonly ClientOption[];
  readonly save: SaveFn<ProjectInput>;
  /** The server's current project (null if it can't be fetched); the store then holds its new row version. */
  readonly refresh?: () => Observable<Project | null>;
}

/** Creates a project, or edits the one passed in. Stays open, keeping the values, until the save succeeds. */
@Component({
  selector: 'app-project-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    TranslocoDirective,
    StaleNotice,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'projects'">
      <h2 mat-dialog-title>{{ data.project ? t('editTitle') : t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          @if (stale()) {
            <app-stale-notice [message]="t('staleMessage')" [busy]="saving()" (reload)="reload()" (overwrite)="overwrite()" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('client') }}</mat-label>
            <mat-select formControlName="clientId" required>
              @for (client of clients; track client.id) {
                <mat-option [value]="client.id">{{ client.name }}</mat-option>
              }
            </mat-select>
            @if (clients.length === 0) {
              <mat-hint>{{ t('noClients') }}</mat-hint>
            }
            @if (form.controls.clientId.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.clientId.invalid) {
              <mat-error>{{ t('clientRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" required />
            @if (form.controls.name.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('description') }}</mat-label>
            <textarea matInput formControlName="description" rows="3"></textarea>
            @if (form.controls.description.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            }
          </mat-form-field>
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
          <button mat-flat-button type="submit" [disabled]="saving()">{{ t('save') }}</button>
        </mat-dialog-actions>
      </form>
    </ng-container>
  `,
  styles: `
    mat-form-field {
      width: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectFormDialog {
  private readonly dialogRef = inject<MatDialogRef<ProjectFormDialog, true>>(MatDialogRef);
  protected readonly data = inject<ProjectFormData>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly saving = signal(false);
  /** Someone else changed the project while this dialog was open. */
  protected readonly stale = signal(false);

  /** The project's current client stays selectable even when it isn't among the active ones. */
  protected readonly clients: readonly ClientOption[] =
    this.data.project && !this.data.clients.some((client) => client.id === this.data.project?.clientId)
      ? [{ id: this.data.project.clientId, name: this.data.project.clientName }, ...this.data.clients]
      : this.data.clients;

  protected readonly form = inject(NonNullableFormBuilder).group({
    clientId: [this.data.project?.clientId ?? '', Validators.required],
    name: [this.data.project?.name ?? '', [Validators.required, Validators.minLength(3), Validators.maxLength(100)]],
    description: [this.data.project?.description ?? '', Validators.maxLength(2000)],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { clientId, name, description } = this.form.getRawValue();
    this.saving.set(true);
    this.stale.set(false);
    this.data
      .save({ clientId, name: name.trim(), description: description.trim() || null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((error) => {
        this.saving.set(false);
        if (isStale(error)) {
          this.stale.set(true);
        } else if (error) {
          applyApiFieldErrors(this.form, error);
        } else {
          this.dialogRef.close(true);
        }
      });
  }

  /** Replaces the form with the server's version, losing what was typed. */
  protected reload(): void {
    this.fetchLatest((project) => {
      this.form.reset({
        clientId: project.clientId,
        name: project.name,
        description: project.description ?? '',
      });
    });
  }

  /** Takes the server's row version, then saves the typed values again. */
  protected overwrite(): void {
    this.fetchLatest(() => this.submit());
  }

  private fetchLatest(then: (project: Project) => void): void {
    const refresh = this.data.refresh;
    if (!refresh) {
      return;
    }
    this.saving.set(true);
    refresh()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((project) => {
        this.saving.set(false);
        if (project) {
          this.stale.set(false);
          then(project);
        }
      });
  }
}
