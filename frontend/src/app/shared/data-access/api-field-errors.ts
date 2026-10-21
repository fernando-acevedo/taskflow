import { FormGroup } from '@angular/forms';
import { ApiError } from '../../core/http/api-error';

/**
 * Shows the API's validation messages under the matching fields (`ApiError.fields` is keyed by camelCase
 * property name, like the form controls). The message is kept as the `api` error until the field changes.
 */
export function applyApiFieldErrors(form: FormGroup, error: ApiError): void {
  for (const [field, messages] of Object.entries(error.fields)) {
    const control = form.get(field);
    if (control && messages.length > 0) {
      control.setErrors({ ...control.errors, api: messages[0] });
      control.markAsTouched();
    }
  }
}
