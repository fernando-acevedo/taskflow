import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { authFeature } from '@core/auth/store/auth.feature';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import { LanguageService } from '@core/i18n/language.service';
import { Notifier } from '@core/notifications/notifier';
import { EmptyState } from '@shared/ui/empty-state';
import { formatMinutes } from '@shared/ui/duration';
import { DashboardPageActions } from '../data-access/store/dashboard.actions';
import { myDashboardFeature } from '../data-access/store/my-dashboard.feature';
import { Column, ColumnChart } from '../ui/column-chart';
import { StatTile } from '../ui/stat-tile';

/** Short weekday names, Monday first, in the given locale. 2024-01-01 was a Monday. */
export function weekdayLabels(locale: string): string[] {
  const format = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
  return Array.from({ length: 7 }, (_, day) => format.format(new Date(Date.UTC(2024, 0, 1 + day))));
}

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
    ColumnChart,
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
  protected readonly canManage = this.store.selectSignal(tenantFeature.selectCanManage);
  private readonly tenantId = this.store.selectSignal(tenantFeature.selectCurrentId);
  protected readonly dashboard = this.store.selectSignal(myDashboardFeature.selectDashboard);
  protected readonly loading = this.store.selectSignal(myDashboardFeature.selectLoading);
  protected readonly error = this.store.selectSignal(myDashboardFeature.selectError);

  protected readonly format = formatMinutes;
  /** The seven days of this week, Monday to Sunday, with the time logged on each. */
  protected readonly weekColumns = computed<Column[]>(() => {
    const labels = weekdayLabels(this.language.current());
    const minutes = this.dashboard()?.weekMinutesByDay ?? [];
    return labels.map((label, day) => ({ key: String(day), label, value: minutes[day] ?? 0, text: formatMinutes(minutes[day] ?? 0) }));
  });

  constructor() {
    // Loads on opening and again when the tenant changes while the home page is already showing.
    effect(() => {
      this.tenantId();
      untracked(() => this.refresh());
    });
  }

  protected refresh(): void {
    this.store.dispatch(DashboardPageActions.opened());
  }
}
