import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/** 0 = empty, then weak, fair, good and strong. */
export type PasswordStrength = 0 | 1 | 2 | 3 | 4;

/**
 * A quick estimate from length and character variety, to guide the user while typing; the API's password
 * policy is what decides whether a password is accepted.
 */
export function passwordStrength(password: string): PasswordStrength {
  if (!password) {
    return 0;
  }
  // One character repeated, or a run like "12345678" or "abcdefgh", is weak whatever its length.
  if (/^(.)\1*$/.test(password) || isSequence(password)) {
    return 1;
  }

  const variety = [/[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z0-9]/].filter((pattern) => pattern.test(password)).length;
  let score = 0;
  if (password.length >= 8) {
    score++;
  }
  if (password.length >= 12) {
    score++;
  }
  if (variety >= 3) {
    score++;
  }
  if (variety === 4 || password.length >= 16) {
    score++;
  }
  return Math.min(Math.max(score, 1), 4) as PasswordStrength;
}

function isSequence(password: string): boolean {
  const codes = [...password.toLowerCase()].map((char) => char.charCodeAt(0));
  const step = codes[1] - codes[0];
  return Math.abs(step) === 1 && codes.every((code, index) => index === 0 || code - codes[index - 1] === step);
}

const LEVELS = ['', 'weak', 'fair', 'good', 'strong'] as const;

/** A four-segment bar with a label, announced politely to screen readers as it changes. */
@Component({
  selector: 'app-password-strength',
  imports: [TranslocoDirective],
  template: `
    <ng-container *transloco="let t; prefix: 'passwordStrength'">
      <div class="meter" [attr.data-level]="level()" aria-hidden="true">
        @for (segment of segments; track segment) {
          <span [class.filled]="segment <= strength()"></span>
        }
      </div>
      <p class="label" aria-live="polite">
        @if (strength() > 0) {
          {{ t('label') }}: <strong>{{ t(level()) }}</strong>
        }
      </p>
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
      margin: 0 0 12px;
      --level-color: var(--mat-sys-outline-variant);
    }

    .meter {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 4px;

      &[data-level='weak'] {
        --level-color: var(--mat-sys-error);
      }
      &[data-level='fair'] {
        --level-color: #eb6834;
      }
      &[data-level='good'] {
        --level-color: #eda100;
      }
      &[data-level='strong'] {
        --level-color: #1baf7a;
      }

      span {
        height: 6px;
        border-radius: 3px;
        background: var(--mat-sys-surface-container-highest);
        transition: background-color 150ms ease;

        &.filled {
          background: var(--level-color);
        }
      }
    }

    .label {
      min-height: 1.5em;
      margin: 6px 0 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasswordStrengthMeter {
  readonly password = input.required<string>();

  protected readonly segments = [1, 2, 3, 4];
  protected readonly strength = computed(() => passwordStrength(this.password()));
  protected readonly level = computed(() => LEVELS[this.strength()]);
}
