import { AbstractControl, FormControl, FormGroup, NonNullableFormBuilder, ValidationErrors, Validators } from '@angular/forms';

/** Mirrors the API's password policy so most mistakes are caught before the request. */
export const PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$/;

/** Group validator for forms with `password` and `confirmPassword` controls. */
export function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password: unknown = group.get('password')?.value;
  const confirmation: unknown = group.get('confirmPassword')?.value;
  return password && confirmation && password !== confirmation ? { passwordsMismatch: true } : null;
}

export type NewPasswordGroup = FormGroup<{
  password: FormControl<string>;
  confirmPassword: FormControl<string>;
}>;

/** A new password and its confirmation, as `<app-new-password-fields>` renders them. */
export function newPasswordGroup(fb: NonNullableFormBuilder): NewPasswordGroup {
  return fb.group(
    {
      password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );
}
