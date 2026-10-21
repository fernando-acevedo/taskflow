import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, linkedSignal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
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
  /** Bound from the ?email= and ?token= query parameters of the emailed link. */
  readonly email = input<string>();
  readonly token = input<string>();

  private readonly store = inject(Store);
  private readonly router = inject(Router);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly errors = this.store.selectSignal(authFeature.selectFormErrors);

  /** The token keeps its first value: it's removed from the address bar on load, which clears the input. */
  private readonly linkToken = linkedSignal<string | undefined, string | undefined>({
    source: this.token,
    computation: (token, previous) => token ?? previous?.value,
  });
  protected readonly linkIsComplete = computed(() => !!this.email() && !!this.linkToken());

  protected readonly form = newPasswordGroup(inject(NonNullableFormBuilder));

  constructor() {
    this.store.dispatch(InvitationPageActions.opened());
  }

  ngOnInit(): void {
    // The token works like a one-time password: keep it out of the browser history and the router state.
    if (this.token()) {
      void this.router.navigate([], { queryParams: { token: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }
  }

  protected submit(): void {
    const email = this.email();
    const token = this.linkToken();
    if (this.form.invalid || !email || !token) {
      this.form.markAllAsTouched();
      return;
    }
    const { password } = this.form.getRawValue();
    this.store.dispatch(InvitationPageActions.passwordSubmitted({ acceptance: { email, token, password } }));
  }
}
