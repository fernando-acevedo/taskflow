import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { readEmailedLink } from '../../../core/auth/emailed-link';
import { ConfirmEmailPageActions } from '../../../core/auth/store/auth.actions';
import { authFeature } from '../../../core/auth/store/auth.feature';

/** Where the email sent on registration links to: confirms the email as soon as it opens. */
@Component({
  selector: 'app-confirm-email-page',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatProgressBarModule, TranslocoDirective],
  templateUrl: './confirm-email-page.html',
  styleUrl: './auth-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmEmailPage implements OnInit {
  private readonly store = inject(Store);
  private readonly router = inject(Router);
  protected readonly pending = this.store.selectSignal(authFeature.selectPending);
  protected readonly status = this.store.selectSignal(authFeature.selectEmailConfirmation);
  protected readonly signedIn = this.store.selectSignal(authFeature.selectIsAuthenticated);

  /** The emailed link is `/confirm-email#userId=…&code=…`; it's read once here and then removed from the address bar. */
  private readonly link = readEmailedLink(inject(ActivatedRoute).snapshot.fragment);
  private readonly userId = this.link.get('userId');
  private readonly code = this.link.get('code');
  protected readonly linkIsComplete = !!this.userId && !!this.code;

  constructor() {
    if (this.userId && this.code) {
      this.store.dispatch(
        ConfirmEmailPageActions.linkOpened({ confirmation: { userId: this.userId, confirmationCode: this.code } }),
      );
    }
  }

  ngOnInit(): void {
    if (this.code) {
      void this.router.navigate([], { replaceUrl: true });
    }
  }
}
