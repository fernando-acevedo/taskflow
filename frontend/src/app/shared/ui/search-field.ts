import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

/**
 * The search box of the list pages. Searches on Enter and when the box is cleared with its × button. Chrome
 * and Safari fire both `keyup.enter` and `search` for one Enter, so a text equal to the last one searched is
 * ignored. Firefox only fires `keyup.enter`.
 */
@Component({
  selector: 'app-search-field',
  imports: [MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic">
      <mat-label>{{ label() }}</mat-label>
      <mat-icon matPrefix aria-hidden="true">search</mat-icon>
      <input #box matInput type="search" [value]="value()" (keyup.enter)="submit(box.value)" (search)="submit(box.value)" />
    </mat-form-field>
  `,
  styles: `
    // The form field takes part in the page's .filters layout directly.
    :host {
      display: contents;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchField {
  readonly label = input.required<string>();
  /** The search in effect, from the store or the URL. */
  readonly value = input('');
  readonly searched = output<string>();

  private readonly lastSearched = linkedSignal(() => this.value());

  protected submit(text: string): void {
    const search = text.trim();
    if (search === this.lastSearched()) {
      return;
    }
    this.lastSearched.set(search);
    this.searched.emit(search);
  }
}
