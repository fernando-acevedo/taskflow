import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { formatMinutes } from '../../../shared/ui/duration';

/**
 * Logged time against the estimate in one horizontal bar. The numbers are always text next to it: "2 h of 4 h",
 * or just the total when the task has no estimate. Going over the estimate adds an icon and the words "over by"
 * to the error color, so the state never rests on color alone.
 */
@Component({
  selector: 'app-hours-bar',
  imports: [MatIconModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'time'">
      @if (estimated() !== null) {
        <div class="track" aria-hidden="true">
          <span class="fill" [class.over]="over()" [style.width.%]="percent()"></span>
        </div>
      }
      <span class="text">
        @if (estimated() === null) {
          <strong>{{ formatMinutes(actual()) }}</strong> {{ t('noEstimate') }}
        } @else {
          <strong>{{ t('of', { actual: formatMinutes(actual()), estimate: formatMinutes(estimated()!) }) }}</strong>
          @if (over()) {
            <span class="over-text">
              <mat-icon aria-hidden="true">warning</mat-icon>
              {{ t('overBy', { time: formatMinutes(actual() - estimated()!) }) }}
            </span>
          }
        }
      </span>
    </ng-container>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .track {
      height: 10px;
      overflow: hidden;
      border-radius: 4px;
      background: var(--tf-chart-track);
    }
    .fill {
      display: block;
      height: 100%;
      min-width: 2px;
      border-radius: 4px;
      background: var(--tf-chart-bar);
    }
    .fill.over {
      background: var(--mat-sys-error);
    }
    .text {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 12px;
      font-variant-numeric: tabular-nums;
    }
    .over-text {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      color: var(--mat-sys-error);
    }
    mat-icon {
      width: 16px;
      height: 16px;
      font-size: 16px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HoursBar {
  readonly actual = input.required<number>();
  /** Null when the task has no estimate. */
  readonly estimated = input.required<number | null>();

  protected readonly formatMinutes = formatMinutes;
  protected readonly over = computed(() => {
    const estimated = this.estimated();
    return estimated !== null && this.actual() > estimated;
  });
  protected readonly percent = computed(() => {
    const estimated = this.estimated();
    return estimated === null ? 0 : Math.min(100, (this.actual() * 100) / Math.max(1, estimated));
  });
}
