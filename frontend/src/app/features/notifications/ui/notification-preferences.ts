import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { Notifier } from '@core/notifications/notifier';
import { NotificationPreferencesActions } from '../data-access/store/notifications.actions';
import { notificationsFeature } from '../data-access/store/notifications.feature';

/** The email preferences card of the profile page. */
@Component({
  selector: 'app-notification-preferences',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatProgressBarModule, MatSlideToggleModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'notifications.preferences'">
      <mat-card appearance="outlined">
        @if (loading() || saving()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <mat-card-header>
          <mat-card-title>{{ t('title') }}</mat-card-title>
          <mat-card-subtitle>{{ t('hint') }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (loadError(); as error) {
            <p class="error" role="alert">{{ notifier.errorMessage(error) }}</p>
            <button mat-stroked-button type="button" (click)="load()">{{ t('retry') }}</button>
          } @else {
            <form [formGroup]="form" (ngSubmit)="submit()">
              <mat-slide-toggle formControlName="emailOnAssigned">{{ t('emailOnAssigned') }}</mat-slide-toggle>
              <mat-slide-toggle formControlName="emailOnMentioned">{{ t('emailOnMentioned') }}</mat-slide-toggle>
              <mat-slide-toggle formControlName="emailOnCommentOnMyTask">{{ t('emailOnCommentOnMyTask') }}</mat-slide-toggle>
              <mat-slide-toggle formControlName="emailDailyDigest">{{ t('emailDailyDigest') }}</mat-slide-toggle>
              <div class="actions">
                <button mat-flat-button type="submit" [disabled]="loading() || saving() || form.pristine">{{ t('save') }}</button>
              </div>
            </form>
          }
        </mat-card-content>
      </mat-card>
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
      margin-top: 24px;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin-top: 16px;
    }
    .actions {
      margin-top: 8px;
    }
    .error {
      color: var(--mat-sys-error);
    }
  `,
})
export class NotificationPreferencesCard {
  private readonly store = inject(Store);
  protected readonly notifier = inject(Notifier);
  protected readonly loading = this.store.selectSignal(notificationsFeature.selectPreferencesLoading);
  protected readonly saving = this.store.selectSignal(notificationsFeature.selectPreferencesSaving);
  protected readonly loadError = this.store.selectSignal(notificationsFeature.selectPreferencesError);
  private readonly preferences = this.store.selectSignal(notificationsFeature.selectPreferences);

  protected readonly form = inject(NonNullableFormBuilder).group({
    emailOnAssigned: false,
    emailOnMentioned: false,
    emailOnCommentOnMyTask: false,
    emailDailyDigest: false,
  });

  constructor() {
    this.load();

    // Keep the toggles in sync with what the API stored (first load and after a save).
    effect(() => {
      const preferences = this.preferences();
      if (preferences) {
        this.form.reset(preferences);
      }
    });
  }

  protected load(): void {
    this.store.dispatch(NotificationPreferencesActions.opened());
  }

  protected submit(): void {
    this.store.dispatch(NotificationPreferencesActions.saveSubmitted({ preferences: this.form.getRawValue() }));
  }
}
