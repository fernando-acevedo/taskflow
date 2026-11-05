import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Label } from '../data-access/labels-api.service';
import { textColorFor } from './label-colors';

/** A label as a colored chip; the text color is picked for contrast with the label's color. */
@Component({
  selector: 'app-label-chip',
  template: `{{ label().name }}`,
  host: {
    '[style.background-color]': 'label().color',
    '[style.color]': 'textColor()',
  },
  styles: `
    :host {
      display: inline-block;
      max-width: 100%;
      padding: 1px 8px;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabelChip {
  readonly label = input.required<Pick<Label, 'name' | 'color'>>();
  protected readonly textColor = computed(() => textColorFor(this.label().color));
}
