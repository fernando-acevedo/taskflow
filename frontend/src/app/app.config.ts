import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { provideEffects } from '@ngrx/effects';
import { provideRouterStore, routerReducer } from '@ngrx/router-store';
import { provideStore } from '@ngrx/store';
import { provideStoreDevtools } from '@ngrx/store-devtools';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { hidePasswords } from './core/auth/devtools-sanitizer';
import { restoreSession } from './core/auth/session';
import * as authEffects from './core/auth/store/auth.effects';
import { authFeature } from './core/auth/store/auth.feature';
import { acceptLanguageInterceptor } from './core/i18n/accept-language.interceptor';
import {
  DEFAULT_LANGUAGE,
  SUPPORTED_LANGUAGES,
  readSavedLanguage,
  resolveInitialLanguage,
} from './core/i18n/languages';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([acceptLanguageInterceptor, authInterceptor])),
    provideStore({ router: routerReducer, [authFeature.name]: authFeature.reducer }),
    provideEffects(authEffects),
    provideRouterStore(),
    provideStoreDevtools({ maxAge: 25, logOnly: !isDevMode(), actionSanitizer: hidePasswords }),
    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: resolveInitialLanguage(readSavedLanguage(), navigator.languages),
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    provideAppInitializer(restoreSession),
  ],
};
