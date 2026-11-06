import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface Column {
  readonly key: string;
  /** Under the column, such as a weekday. */
  readonly label: string;
  readonly value: number;
  /** Over the column; defaults to the value. */
  readonly text?: string;
}

/**
 * Magnitudes over a sequence (days of a week) as vertical columns in one hue, scaled to the largest value (or
 * `max`). Each column shows its value as text above it, so the columns are a visual aid and the numbers are
 * readable without hovering; the spoken summary lists them all.
 */
@Component({
  selector: 'app-column-chart',
  template: `
    <div class="chart" role="img" [attr.aria-label]="summary()">
      @for (column of columns(); track column.key) {
        <div class="column" aria-hidden="true">
          <span class="value">{{ column.text ?? column.value }}</span>
          <span class="plot">
            <span class="fill" [style.height.%]="(column.value * 100) / scale()"></span>
          </span>
          <span class="label">{{ column.label }}</span>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .chart {
      display: grid;
      grid-auto-flow: column;
      grid-auto-columns: minmax(0, 1fr);
      gap: 6px;
    }
    .column {
      display: flex;
      flex-direction: column;
      align-items: stretch;
      gap: 4px;
      min-width: 0;
      text-align: center;
    }
    .plot {
      display: flex;
      align-items: flex-end;
      height: 72px;
      border-bottom: 1px solid var(--tf-chart-track);
    }
    .fill {
      display: block;
      width: 100%;
      min-height: 2px;
      border-radius: 4px 4px 0 0;
      background: var(--tf-chart-bar);
    }
    .value {
      display: flex;
      align-items: flex-end;
      justify-content: center;
      /* Room for "2 h 30 min" to wrap on two lines without moving the columns. */
      min-height: 2.6em;
      font: var(--mat-sys-label-small);
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      overflow-wrap: anywhere;
    }
    .label {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-small);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ColumnChart {
  readonly columns = input.required<readonly Column[]>();
  /** The value of a full column; defaults to the largest one. */
  readonly max = input<number | null>(null);

  protected readonly scale = computed(() => Math.max(1, this.max() ?? Math.max(0, ...this.columns().map((column) => column.value))));
  protected readonly summary = computed(() =>
    this.columns()
      .map((column) => `${column.label} ${column.text ?? column.value}`)
      .join(', '),
  );
}
