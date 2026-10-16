import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoDirective } from '@jsverse/transloco';
import { Store } from '@ngrx/store';
import { LanguageService } from '../../../core/i18n/language.service';
import { environment } from '../../../../environments/environment';
import { StatusPageActions } from '../data-access/store/status.actions';
import { statusFeature } from '../data-access/store/status.feature';

@Component({
  selector: 'app-status-page',
  imports: [DatePipe, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, TranslocoDirective],
  templateUrl: './status-page.html',
  styleUrl: './status-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPage {
  private readonly store = inject(Store);
  protected readonly language = inject(LanguageService);

  protected readonly apiUrl = environment.apiUrl;
  protected readonly overallStatus = this.store.selectSignal(statusFeature.selectOverallStatus);
  protected readonly health = this.store.selectSignal(statusFeature.selectHealth);
  protected readonly info = this.store.selectSignal(statusFeature.selectInfo);
  protected readonly loading = this.store.selectSignal(statusFeature.selectLoading);
  protected readonly checkedAt = this.store.selectSignal(statusFeature.selectCheckedAt);

  constructor() {
    // Reload on every language change: the API message comes back in the new language.
    toObservable(this.language.current)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.refresh());
  }

  protected refresh(): void {
    this.store.dispatch(StatusPageActions.statusRequested());
  }
}
