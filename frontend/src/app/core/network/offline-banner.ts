import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { OnlineStatus } from './online-status';

/**
 * "You are offline" strip for the shell. The live region (`role="status"`) is always in the page, empty while
 * online, so a screen reader announces the text when it appears.
 */
@Component({
  selector: 'app-offline-banner',
  imports: [MatIconModule, TranslocoDirective],
  template: `
    <div class="offline-banner-region" role="status">
      @if (!status.online()) {
        <p class="offline-banner" *transloco="let t">
          <mat-icon aria-hidden="true">cloud_off</mat-icon>
          <span>{{ t('app.offline') }}</span>
        </p>
      }
    </div>
  `,
  styles: `
    .offline-banner {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      margin: 0;
      padding: 8px 16px;
      font: var(--mat-sys-label-large);
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OfflineBanner {
  protected readonly status = inject(OnlineStatus);
}
