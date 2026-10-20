import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { newPasswordGroup } from '../../../core/auth/password-rules';
import { RegisterPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { ErrorTextPipe } from '../../../core/i18n/error-text.pipe';
import { NewPasswordFields } from '../../../shared/ui/new-password-fields';

@Component({
  selector: 'app-register-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    TranslocoDirective,
    ErrorTextPipe,
    NewPasswordFields,
  ],
  templateUrl: './register-page.html',
  styleUrl: './auth-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPage {
  private readonly store = inject(Store);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);

  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    passwords: newPasswordGroup(this.fb),
  });

  constructor() {
    this.store.dispatch(RegisterPageActions.opened());
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { email, passwords } = this.form.getRawValue();
    this.store.dispatch(RegisterPageActions.registerSubmitted({ credentials: { email, password: passwords.password } }));
  }
}
