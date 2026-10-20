import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatTooltipModule } from '@angular/material/tooltip';

export interface StatusSegment {
  readonly key: string;
  readonly label: string;
  readonly count: number;
  /** A CSS color, normally one of the `--tf-chart-*` variables. */
  readonly color: string;
}

/**
 * Part-to-whole in one horizontal stacked bar: segments in a fixed order, 2px gaps between them, rounded outer
 * ends. The legend below names every segment with its count and share, so identity never rests on color alone
 * and the numbers are readable without hovering.
 */
@Component({
  selector: 'app-status-bar',
  imports: [MatTooltipModule],
  template: `
    @if (total() > 0) {
      <div class="bar" role="img" [attr.aria-label]="summary()">
        @for (segment of visible(); track segment.key) {
          <span
            class="segment"
            [style.flex-grow]="segment.count"
            [style.background]="segment.color"
            [matTooltip]="segment.label + ': ' + segment.count + ' (' + percent(segment.count) + '%)'"
          ></span>
        }
      </div>
    } @else {
      <div class="bar empty" aria-hidden="true"></div>
    }
    <ul class="legend">
      @for (segment of segments(); track segment.key) {
        <li>
          <span class="swatch" [style.background]="segment.color" aria-hidden="true"></span>
          <span class="name">{{ segment.label }}</span>
          <span class="count">{{ segment.count }}</span>
          @if (total() > 0) {
            <span class="share">{{ percent(segment.count) }}%</span>
          }
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: block;
    }
    .bar {
      display: flex;
      gap: 2px;
      height: 16px;
      overflow: hidden;
      border-radius: 4px;
    }
    .bar.empty {
      background: var(--tf-chart-track);
    }
    .segment {
      flex-basis: 0;
      min-width: 4px;
    }
    .legend {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: 8px 16px;
      margin: 12px 0 0;
      padding: 0;
      list-style: none;
    }
    .legend li {
      display: flex;
      align-items: baseline;
      gap: 6px;
    }
    .swatch {
      flex: none;
      width: 10px;
      height: 10px;
      border-radius: 2px;
      align-self: center;
    }
    .name {
      color: var(--mat-sys-on-surface-variant);
    }
    .count {
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }
    .share {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusBar {
  readonly segments = input.required<readonly StatusSegment[]>();
  /** Spoken summary for screen readers, e.g. "To do 3, In progress 1". Built from the segments when empty. */
  readonly label = input('');

  protected readonly total = computed(() => this.segments().reduce((sum, segment) => sum + segment.count, 0));
  protected readonly visible = computed(() => this.segments().filter((segment) => segment.count > 0));
  protected readonly summary = computed(
    () => this.label() || this.segments().map((segment) => `${segment.label} ${segment.count}`).join(', '),
  );

  protected percent(count: number): number {
    return Math.round((count * 100) / Math.max(1, this.total()));
  }
}
