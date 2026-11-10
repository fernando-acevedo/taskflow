import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoDirective } from '@jsverse/transloco';
import { DemoAccount, DemoService } from '@core/demo/demo.service';
import { LanguageService } from '@core/i18n/language.service';

/** Lists the public demo's accounts on the login page; renders nothing when the demo is off. */
@Component({
  selector: 'app-demo-panel',
  imports: [DatePipe, MatButtonModule, TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let demo = status();
    @if (demo.enabled) {
      <section class="demo-panel" *transloco="let t" [attr.aria-label]="t('demo.title')">
        <h2>{{ t('demo.title') }}</h2>
        <ul>
          @for (account of demo.accounts; track account.email) {
            <li>
              <span class="role">{{ account.role }}</span>
              <code>{{ account.email }}</code>
              <code>{{ account.password }}</code>
              <button mat-stroked-button type="button" (click)="selected.emit(account)">{{ t('demo.useAccount') }}</button>
            </li>
          }
        </ul>
        @if (demo.nextResetAt; as reset) {
          <p class="reset">{{ t('demo.resetsAt', { when: (reset | date: 'medium' : undefined : language.current()) }) }}</p>
        }
      </section>
    }
  `,
  styles: `
    .demo-panel {
      max-width: 420px;
      margin: 16px auto 0;
      box-sizing: border-box;
      padding: 16px;
      border-radius: var(--mat-sys-corner-medium);
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
    h2 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-small);
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 12px;
      padding: 4px 0;
    }
    .role {
      min-width: 72px;
      font-weight: 500;
    }
    code {
      font-size: 0.85em;
    }
    button {
      margin-left: auto;
    }
    .reset {
      margin: 8px 0 0;
      font: var(--mat-sys-body-small);
    }
  `,
})
export class DemoPanel {
  protected readonly language = inject(LanguageService);
  protected readonly status = inject(DemoService).status;
  readonly selected = output<DemoAccount>();
}
