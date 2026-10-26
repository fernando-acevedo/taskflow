import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { readEmailedLink } from '../../../core/auth/emailed-link';
import { newPasswordGroup } from '../../../core/auth/password-rules';
import { ResetPasswordPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { ErrorTextPipe } from '../../../core/i18n/error-text.pipe';
import { NewPasswordFields } from '../../../shared/ui/new-password-fields';

/** Where the password reset email links to: the user chooses a new password, then is signed in. */
@Component({
  selector: 'app-reset-password-page',
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
  templateUrl: './reset-password-page.html',
  styleUrl: './auth-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordPage implements OnInit {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);

  /** The emailed link is `/reset-password#email=…&code=…`; it's read once here and then removed from the address bar. */
  private readonly link = readEmailedLink(inject(ActivatedRoute).snapshot.fragment);
  protected readonly email = this.link.get('email');
  private readonly code = this.link.get('code');
  protected readonly linkIsComplete = !!this.email && !!this.code;

  protected readonly form = newPasswordGroup(inject(NonNullableFormBuilder));

  constructor() {
    this.store.dispatch(ResetPasswordPageActions.opened());
  }

  ngOnInit(): void {
    // The code works like a one-time password: keep it out of the browser history and the router state.
    if (this.code) {
      void this.router.navigate([], { replaceUrl: true });
    }
  }

  protected submit(): void {
    const { email, code } = this;
    if (this.form.invalid || !email || !code) {
      this.form.markAllAsTouched();
      return;
    }
    const { password } = this.form.getRawValue();
    this.store.dispatch(
      ResetPasswordPageActions.passwordSubmitted({ reset: { email, resetCode: code, newPassword: password } }),
    );
  }
}
