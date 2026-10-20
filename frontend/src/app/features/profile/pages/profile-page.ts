import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { ProfilePageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';

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
  ],
  templateUrl: './profile-page.html',
  styleUrl: './profile-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage {
  private readonly store = inject(Store);
  protected readonly user = this.store.selectSignal(authFeature.selectUser);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);
  protected readonly saved = this.store.selectSignal(authFeature.selectProfileSaved);

  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
  });

  constructor() {
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

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.store.dispatch(ProfilePageActions.displayNameSubmitted({ displayName: this.form.controls.displayName.value }));
  }
}
