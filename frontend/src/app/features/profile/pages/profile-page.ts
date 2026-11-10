import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, InjectionToken, Type, effect, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { ProfilePageActions } from '@core/auth/store/auth.actions';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import { authFeature } from '@core/auth/store/auth.feature';
import { ErrorTextPipe } from '@core/i18n/error-text.pipe';
import { ThemePreferenceCard } from '@core/theme/theme-preference-card';
import { HasUnsavedChanges, warnBeforeUnload } from '@shared/ui/unsaved-changes';
import { ApiKeysCard } from '@features/api-keys';
import { loadNotificationPreferencesCard } from '@features/notifications';

/** How the notification preferences card is loaded; a test replaces it. */
export const PREFERENCES_CARD_LOADER = new InjectionToken<() => Promise<Type<unknown>>>('PREFERENCES_CARD_LOADER', {
  providedIn: 'root',
  factory: () => loadNotificationPreferencesCard,
});

@Component({
  selector: 'app-profile-page',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
    ErrorTextPipe,
    ApiKeysCard,
    NgComponentOutlet,
    ThemePreferenceCard,
  ],
  templateUrl: './profile-page.html',
  styleUrl: './profile-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage implements HasUnsavedChanges {
  private readonly store = inject(Store);
  private readonly loadCard = inject(PREFERENCES_CARD_LOADER);
  /** The notification preferences card, loaded on demand (see the notifications barrel). */
  protected readonly preferencesCard = signal<Type<unknown> | null>(null);
  /** The chunk with the card failed to download: the rest of the page still works, and the user can retry. */
  protected readonly preferencesFailed = signal(false);
  protected readonly user = this.store.selectSignal(authFeature.selectUser);
  protected readonly isAdmin = this.store.selectSignal(authFeature.selectIsAdmin);
  protected readonly tenantRole = this.store.selectSignal(tenantFeature.selectRole);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);
  protected readonly saved = this.store.selectSignal(authFeature.selectProfileSaved);
  protected readonly confirmationEmailSent = this.store.selectSignal(authFeature.selectConfirmationEmailSent);

  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
  });

  constructor() {
    warnBeforeUnload(() => this.hasUnsavedChanges());
    this.loadPreferences();
    this.store.dispatch(ProfilePageActions.opened());

    // Keep the field in sync with the saved name (initial load and after a save).
    effect(() => {
      const displayName = this.user()?.displayName;
      if (displayName !== undefined) {
        this.form.controls.displayName.setValue(displayName);
        this.form.markAsPristine();
      }
    });
  }

  protected loadPreferences(): void {
    this.preferencesFailed.set(false);
    this.loadCard().then(
      (card) => this.preferencesCard.set(card),
      () => this.preferencesFailed.set(true),
    );
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.store.dispatch(ProfilePageActions.displayNameSubmitted({ displayName: this.form.controls.displayName.value }));
  }

  protected resendConfirmation(email: string): void {
    this.store.dispatch(ProfilePageActions.confirmationEmailRequested({ email }));
  }
}
