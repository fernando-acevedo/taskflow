import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Observable } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import { isStale } from '@shared/data-access/api-error';
import { applyApiFieldErrors } from '@shared/data-access/api-field-errors';
import { SaveFn } from '@shared/data-access/submission';
import { StaleNotice } from '@shared/ui/stale-notice';
import { Client, ClientInput } from '../data-access/clients-api.service';
import { confirmCloseWhenModified } from '@shared/ui/unsaved-changes';

export interface ClientFormData {
  readonly client: Client | null;
  readonly save: SaveFn<ClientInput>;
  /** The server's current client (null if it can't be fetched); the store then holds its new row version. */
  readonly refresh?: () => Observable<Client | null>;
}

/** Creates a client, or edits the one passed as data. Stays open, keeping the values, until the save succeeds. */
@Component({
  selector: 'app-client-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
    StaleNotice,
  ],
  template: `
    <ng-container *transloco="let t; prefix: 'clients'">
      <h2 mat-dialog-title>{{ client ? t('editTitle') : t('createTitle') }}</h2>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <mat-dialog-content>
          @if (saving()) {
            <mat-progress-bar mode="indeterminate" />
          }
          @if (stale()) {
            <app-stale-notice [message]="t('staleMessage')" [busy]="saving()" (reload)="reload()" (overwrite)="overwrite()" />
          }
          <mat-form-field appearance="outline">
            <mat-label>{{ t('name') }}</mat-label>
            <input matInput formControlName="name" required cdkFocusInitial />
            @if (form.controls.name.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.name.invalid) {
              <mat-error>{{ t('nameRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('contactEmail') }}</mat-label>
            <input matInput type="email" formControlName="contactEmail" autocomplete="off" />
            @if (form.controls.contactEmail.getError('api'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.contactEmail.invalid) {
              <mat-error>{{ t('emailRule') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ t('notes') }}</mat-label>
            <textarea matInput formControlName="notes" rows="3"></textarea>
            @if (form.controls.notes.getError('api'); as message) {
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
export class ClientFormDialog {
  private readonly dialogRef = inject<MatDialogRef<ClientFormDialog, true>>(MatDialogRef);
  private readonly data = inject<ClientFormData>(MAT_DIALOG_DATA);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly client = this.data.client;
  protected readonly saving = signal(false);
  /** Someone else changed the client while this dialog was open. */
  protected readonly stale = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.client?.name ?? '', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    contactEmail: [this.client?.contactEmail ?? '', [Validators.email, Validators.maxLength(256)]],
    notes: [this.client?.notes ?? '', [Validators.maxLength(2000)]],
  });

  constructor() {
    confirmCloseWhenModified(this.dialogRef, () => this.form.dirty);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, contactEmail, notes } = this.form.getRawValue();
    this.saving.set(true);
    this.stale.set(false);
    this.data
      .save({ name: name.trim(), contactEmail: contactEmail.trim() || null, notes: notes.trim() || null })
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
    this.fetchLatest((client) => {
      this.form.reset({
        name: client.name,
        contactEmail: client.contactEmail ?? '',
        notes: client.notes ?? '',
      });
    });
  }

  /** Takes the server's row version, then saves the typed values again. */
  protected overwrite(): void {
    this.fetchLatest(() => this.submit());
  }

  private fetchLatest(then: (client: Client) => void): void {
    const refresh = this.data.refresh;
    if (!refresh) {
      return;
    }
    this.saving.set(true);
    refresh()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((client) => {
        this.saving.set(false);
        if (client) {
          this.stale.set(false);
          then(client);
        }
      });
  }
}
