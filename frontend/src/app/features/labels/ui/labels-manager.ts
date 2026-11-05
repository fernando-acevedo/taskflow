import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { filter } from 'rxjs';
import { SaveFn, Submissions, nextSubmissionId } from '../../../shared/data-access/submission';
import { confirm } from '../../../shared/ui/confirm-dialog';
import { Label, LabelInput } from '../data-access/labels-api.service';
import { LabelsPageActions } from '../data-access/store/labels.actions';
import { labelsFeature } from '../data-access/store/labels.feature';
import { LabelChip } from './label-chip';
import { LabelFormData, LabelFormDialog } from './label-form-dialog';

/** The project's labels with create, edit and delete. Shown to owners only (the API requires it too). */
@Component({
  selector: 'app-labels-manager',
  imports: [MatButtonModule, MatCardModule, MatIconModule, TranslocoDirective, LabelChip],
  template: `
    <ng-container *transloco="let t; prefix: 'labels'">
      <mat-card appearance="outlined">
        <mat-card-header>
          <mat-card-title>{{ t('title') }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (labels().length === 0) {
            <p class="empty">{{ t('empty') }}</p>
          } @else {
            <ul class="list">
              @for (label of labels(); track label.id) {
                <li>
                  <app-label-chip [label]="label" />
                  <span class="spacer"></span>
                  <button mat-icon-button type="button" [attr.aria-label]="t('edit', { name: label.name })" (click)="edit(label)" [disabled]="saving()">
                    <mat-icon aria-hidden="true">edit</mat-icon>
                  </button>
                  <button mat-icon-button type="button" [attr.aria-label]="t('delete', { name: label.name })" (click)="remove(label)" [disabled]="saving()">
                    <mat-icon aria-hidden="true">delete</mat-icon>
                  </button>
                </li>
              }
            </ul>
          }
        </mat-card-content>
        <mat-card-actions align="end">
          <button mat-flat-button type="button" (click)="edit(null)" [disabled]="saving()">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ t('create') }}
          </button>
        </mat-card-actions>
      </mat-card>
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
    }
    .list {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    .list li {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 4px 0;
    }
    .spacer {
      flex: 1;
    }
    .empty {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabelsManager {
  private readonly store = inject(Store);
  private readonly submissions = inject(Submissions);
  private readonly dialog = inject(MatDialog);

  protected readonly labels = this.store.selectSignal(labelsFeature.selectAll);
  protected readonly saving = this.store.selectSignal(labelsFeature.selectSaving);

  protected edit(label: Label | null): void {
    const save: SaveFn<LabelInput> = (input) =>
      this.submissions.dispatchAndWait(
        label
          ? LabelsPageActions.updateSubmitted({ labelId: label.id, changes: input, submissionId: nextSubmissionId() })
          : LabelsPageActions.createSubmitted({ label: input, submissionId: nextSubmissionId() }),
      );
    this.dialog.open<LabelFormDialog, LabelFormData>(LabelFormDialog, { data: { label, save }, width: '460px', maxWidth: 'calc(100vw - 32px)' });
  }

  protected remove(label: Label): void {
    confirm(this.dialog, {
      title: 'labels.deleteTitle',
      message: 'labels.deleteMessage',
      confirm: 'labels.deleteConfirm',
      params: { name: label.name },
    })
      .pipe(filter(Boolean))
      .subscribe(() => this.store.dispatch(LabelsPageActions.deleteConfirmed({ labelId: label.id })));
  }
}
