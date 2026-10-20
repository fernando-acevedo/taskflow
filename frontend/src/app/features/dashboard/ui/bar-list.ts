import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

export interface BarRow {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  /** Extra text after the value, such as "1 overdue". Shown with a warning icon when `alert` is set. */
  readonly note?: string;
  readonly alert?: boolean;
}

/**
 * Magnitudes compared as horizontal bars in one hue, scaled to the largest value (or `max`). Every row shows its
 * value as text, so the bars are a visual aid and the list doubles as the table view.
 */
@Component({
  selector: 'app-bar-list',
  imports: [MatIconModule, MatTooltipModule],
  template: `
    <ul>
      @for (row of rows(); track row.key) {
        <li>
          <span class="label">{{ row.label }}</span>
          <span class="track" [matTooltip]="row.label + ': ' + row.value">
            <span class="fill" [style.width.%]="(row.value * 100) / scale()"></span>
          </span>
          <span class="value">
            {{ row.value }}
            @if (row.note) {
              <span class="note" [class.alert]="row.alert">
                @if (row.alert) {
                  <mat-icon aria-hidden="true">warning</mat-icon>
                }
                {{ row.note }}
              </span>
            }
          </span>
        </li>
      }
    </ul>
  `,
  styles: `
    ul {
      display: grid;
      grid-template-columns: minmax(80px, max-content) minmax(80px, 1fr) max-content;
      align-items: center;
      gap: 10px 12px;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: contents;
    }
    .label {
      overflow: hidden;
      max-width: 220px;
      color: var(--mat-sys-on-surface);
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .track {
      height: 12px;
    }
    .fill {
      display: block;
      height: 100%;
      min-width: 2px;
      border-radius: 0 4px 4px 0;
      background: var(--tf-chart-bar);
    }
    .value {
      display: flex;
      align-items: center;
      gap: 8px;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }
    .note {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .note.alert {
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
export class BarList {
  readonly rows = input.required<readonly BarRow[]>();
  /** The value of a full bar; defaults to the largest row. */
  readonly max = input<number | null>(null);

  protected readonly scale = computed(() => Math.max(1, this.max() ?? Math.max(0, ...this.rows().map((row) => row.value))));
}
