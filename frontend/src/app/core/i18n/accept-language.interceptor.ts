import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

/** Sends the active UI language so the API answers validation and error messages in it. */
export const acceptLanguageInterceptor: HttpInterceptorFn = (request, next) => {
  const language = inject(TranslocoService).getActiveLang();
  return next(request.clone({ setHeaders: { 'Accept-Language': language } }));
};
