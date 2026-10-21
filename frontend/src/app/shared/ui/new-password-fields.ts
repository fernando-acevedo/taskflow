import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  input,
  viewChild,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective } from '@jsverse/transloco';
import { startWith, switchMap } from 'rxjs';
import { NewPasswordGroup } from '../../core/auth/password-rules';
import { PasswordStrengthMeter } from './password-strength';

/** The new password, its strength, the policy and the confirmation, shared by sign-up and invitation acceptance. */
@Component({
  selector: 'app-new-password-fields',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, TranslocoDirective, PasswordStrengthMeter],
  templateUrl: './new-password-fields.html',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .rules {
      margin: 0 0 16px;
      padding-left: 20px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }

    .mismatch {
      margin: 0 0 12px;
      padding: 12px 16px;
      border-radius: 8px;
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NewPasswordFields {
  readonly group = input.required<NewPasswordGroup>();
  /** Moves the focus to the password field once rendered, for pages where it's the first thing to fill in. */
  readonly focusOnLoad = input(false);

  private readonly passwordInput = viewChild.required<ElementRef<HTMLInputElement>>('passwordInput');

  protected readonly password = toSignal(
    toObservable(this.group).pipe(
      switchMap((group) => group.controls.password.valueChanges.pipe(startWith(group.controls.password.value))),
    ),
    { initialValue: '' },
  );
  protected readonly controls = computed(() => this.group().controls);

  constructor() {
    afterNextRender(() => {
      if (this.focusOnLoad()) {
        this.passwordInput().nativeElement.focus();
      }
    });
  }
}
