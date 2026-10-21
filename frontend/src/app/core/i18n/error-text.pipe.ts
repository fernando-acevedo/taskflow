import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

/** The text of a key, or of `fallbackKey` when the translation files don't have it (the key would show instead). */
export function translateOr(transloco: TranslocoService, key: string, fallbackKey: string): string {
  const text = transloco.translate(key);
  return text === key ? transloco.translate(fallbackKey) : text;
}

/**
 * Translates a form error code under `errors.`. The codes come from the API (Identity codes, field names), so
 * one the frontend doesn't know yet shows the generic message instead of `errors.SomeCode`. Impure like
 * Transloco's own pipe, so it follows language changes.
 */
@Pipe({ name: 'errorText', pure: false })
export class ErrorTextPipe implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(code: string): string {
    return translateOr(this.transloco, `errors.${code}`, 'errors.unexpected');
  }
}
