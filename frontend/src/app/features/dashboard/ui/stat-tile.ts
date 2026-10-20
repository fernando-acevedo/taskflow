import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/**
 * One headline number. The `alert` tone is for a state that needs attention (overdue work): it adds the icon and
 * the error color to the label, never color alone.
 */
@Component({
  selector: 'app-stat-tile',
  imports: [MatIconModule],
  template: `
    <span class="label">
      @if (tone() === 'alert' && value() > 0) {
        <mat-icon aria-hidden="true">warning</mat-icon>
      }
      {{ label() }}
    </span>
    <strong class="value">{{ value() }}</strong>
    @if (hint()) {
      <span class="hint">{{ hint() }}</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 16px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 12px;
      background: var(--mat-sys-surface);
    }
    .label {
      display: flex;
      align-items: center;
      gap: 4px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-large);
    }
    :host(.alert) .label {
      color: var(--mat-sys-error);
    }
    mat-icon {
      width: 18px;
      height: 18px;
      font-size: 18px;
    }
    .value {
      font: var(--mat-sys-display-small);
      font-variant-numeric: tabular-nums;
    }
    .hint {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
  host: { '[class.alert]': "tone() === 'alert' && value() > 0" },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatTile {
  readonly label = input.required<string>();
  readonly value = input.required<number>();
  readonly hint = input<string>('');
  readonly tone = input<'neutral' | 'alert'>('neutral');
}
