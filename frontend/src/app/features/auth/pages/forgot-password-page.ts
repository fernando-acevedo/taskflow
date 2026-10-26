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
import { ForgotPasswordPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { ErrorTextPipe } from '../../../core/i18n/error-text.pipe';

/** Asks for the email to send a reset link to. The answer is the same whether or not the account exists. */
@Component({
  selector: 'app-forgot-password-page',
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
  ],
  templateUrl: './forgot-password-page.html',
  styleUrl: './auth-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordPage {
  private readonly store = inject(Store);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);
  protected readonly requested = this.store.selectSignal(authFeature.selectResetLinkRequested);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
  });

  constructor() {
    this.store.dispatch(ForgotPasswordPageActions.opened());
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.store.dispatch(ForgotPasswordPageActions.emailSubmitted({ email: this.form.getRawValue().email.trim() }));
  }
}
