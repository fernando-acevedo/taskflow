import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { LanguageService } from '../../../core/i18n/language.service';
import { Notifier } from '../../../core/notifications/notifier';
import { EmptyState } from '../../../shared/ui/empty-state';
import { DashboardPageActions } from '../data-access/store/dashboard.actions';
import { myDashboardFeature } from '../data-access/store/my-dashboard.feature';
import { StatTile } from '../ui/stat-tile';

/** The home page: the user's open tasks across projects, and how each client's projects are doing. */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    TranslocoDirective,
    EmptyState,
    StatTile,
  ],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly store = inject(Store);
  protected readonly notifier = inject(Notifier);
  protected readonly language = inject(LanguageService);

  protected readonly user = this.store.selectSignal(authFeature.selectUser);
  protected readonly isAdmin = this.store.selectSignal(authFeature.selectIsAdmin);
  protected readonly dashboard = this.store.selectSignal(myDashboardFeature.selectDashboard);
  protected readonly loading = this.store.selectSignal(myDashboardFeature.selectLoading);
  protected readonly error = this.store.selectSignal(myDashboardFeature.selectError);

  constructor() {
    this.refresh();
  }

  protected refresh(): void {
    this.store.dispatch(DashboardPageActions.opened());
  }
}
