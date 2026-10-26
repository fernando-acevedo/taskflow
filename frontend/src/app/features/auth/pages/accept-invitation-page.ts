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
import { InvitationPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { ErrorTextPipe } from '../../../core/i18n/error-text.pipe';
import { NewPasswordFields } from '../../../shared/ui/new-password-fields';

/** Where the invitation email links to: the invited user chooses their password, then is signed in. */
@Component({
  selector: 'app-accept-invitation-page',
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
  templateUrl: './accept-invitation-page.html',
  styleUrl: './auth-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AcceptInvitationPage implements OnInit {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);

  /**
   * The emailed link is `/accept-invitation#email=…&token=…`. The fragment never reaches a server (no access
   * logs, no `Referer`); it's read once here and then removed from the address bar.
   */
  private readonly link = readEmailedLink(inject(ActivatedRoute).snapshot.fragment);
  protected readonly email = this.link.get('email');
  private readonly token = this.link.get('token');
  protected readonly linkIsComplete = !!this.email && !!this.token;

  protected readonly form = newPasswordGroup(inject(NonNullableFormBuilder));

  constructor() {
    this.store.dispatch(InvitationPageActions.opened());
  }

  ngOnInit(): void {
    // The token works like a one-time password: keep it out of the browser history and the router state.
    if (this.token) {
      void this.router.navigate([], { replaceUrl: true });
    }
  }

  protected submit(): void {
    const { email, token } = this;
    if (this.form.invalid || !email || !token) {
      this.form.markAllAsTouched();
      return;
    }
    const { password } = this.form.getRawValue();
    this.store.dispatch(InvitationPageActions.passwordSubmitted({ acceptance: { email, token, password } }));
  }
}
