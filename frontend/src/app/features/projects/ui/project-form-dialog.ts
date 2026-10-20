import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { ClientOption, Project, ProjectInput } from '../data-access/projects-api.service';

export interface ProjectFormData {
  project: Project | null;
  clients: ClientOption[];
}

/** Creates a project, or edits the one passed in. Closes with the entered values. */
@Component({
  selector: 'app-project-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'projects'">
      <h2 mat-dialog-title>{{ data.project ? t('editTitle') : t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
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
            @if (form.controls.clientId.invalid) {
              <mat-error>{{ t('clientRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" required />
            @if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('description') }}</mat-label>
            <textarea matInput formControlName="description" rows="3"></textarea>
          </mat-form-field>
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
          <button mat-flat-button type="submit">{{ t('save') }}</button>
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
  private readonly dialogRef = inject<MatDialogRef<ProjectFormDialog, ProjectInput>>(MatDialogRef);
  protected readonly data = inject<ProjectFormData>(MAT_DIALOG_DATA);

  /** The project's current client stays selectable even when it isn't among the active ones. */
  protected readonly clients: ClientOption[] =
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
    this.dialogRef.close({ clientId, name: name.trim(), description: description.trim() || null });
  }
}
