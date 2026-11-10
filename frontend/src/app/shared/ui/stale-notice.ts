import { ChangeDetectionStrategy, Component, ElementRef, effect, input, output, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * Shown inside an edit form when the API answered 409 `Concurrency.Stale`: someone else changed the record while
 * it was open. The form keeps what was typed; the user chooses to take the server's version ("Reload", which
 * discards the edits) or to save the typed values over it ("Save anyway"). It is an alert, and takes focus when
 * it appears so keyboard and screen reader users land on the choice.
 */
@Component({
  selector: 'app-stale-notice',
  imports: [MatButtonModule, TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'common.stale'">
      <div class="stale" role="alert">
        <p>{{ message() }}</p>
        <div class="actions">
          <button #reloadButton mat-stroked-button type="button" [disabled]="busy()" (click)="reload.emit()">
            {{ t('reload') }}
          </button>
          <button mat-flat-button type="button" [disabled]="busy()" (click)="overwrite.emit()">
            {{ t('overwrite') }}
          </button>
        </div>
      </div>
    </ng-container>
  `,
  styles: `
    .stale {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
      border-radius: 8px;
      padding: 12px 16px;
      margin-bottom: 16px;
    }
    p {
      margin: 0 0 8px;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StaleNotice {
  /** The (translated) sentence naming what changed: "Someone else changed this task while you were editing." */
  readonly message = input.required<string>();
  /** Disables both actions while a reload or a save runs. */
  readonly busy = input(false);
  /** Take the server's version, losing the edits. */
  readonly reload = output();
  /** Save the typed values over the server's version. */
  readonly overwrite = output();

  private readonly reloadButton = viewChild<unknown, ElementRef<HTMLButtonElement>>('reloadButton', { read: ElementRef });

  constructor() {
    // The button lives inside the translation block, so it exists a little after the component does.
    effect(() => this.reloadButton()?.nativeElement.focus());
  }
}
