import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  ErrorHandler,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { provideEffects } from '@ngrx/effects';
import { provideRouterStore, routerReducer } from '@ngrx/router-store';
import { provideStore } from '@ngrx/store';
import { provideStoreDevtools } from '@ngrx/store-devtools';
import { routes } from './app.routes';
import { authInterceptor } from '@core/auth/auth.interceptor';
import { TelemetryErrorHandler } from '@core/telemetry/telemetry-error-handler';
import { clientIdInterceptor } from '@core/realtime/client-id.interceptor';
import * as realtimeEffects from '@core/realtime/realtime.effects';
import { retryInterceptor } from '@core/http/retry.interceptor';
import { traceparentInterceptor } from '@core/http/trace.interceptor';
import { hideSecrets, hideStateSecrets } from '@core/auth/devtools-sanitizer';
import { restoreSession } from '@core/auth/session';
import * as tenantEffects from '@core/tenant/store/tenant.effects';
import { tenantFeature } from '@core/tenant/store/tenant.feature';
import * as authEffects from '@core/auth/store/auth.effects';
import { authFeature } from '@core/auth/store/auth.feature';
import { TranslatedTitleStrategy } from '@core/title/translated-title.strategy';
import { acceptLanguageInterceptor } from '@core/i18n/accept-language.interceptor';
import {
  DEFAULT_LANGUAGE,
  SUPPORTED_LANGUAGES,
  readSavedLanguage,
  resolveInitialLanguage,
} from '@core/i18n/languages';
import { TranslocoHttpLoader } from '@core/i18n/transloco-loader';
import { notificationsEffects, notificationsFeature } from '@features/notifications';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: TelemetryErrorHandler },
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useExisting: TranslatedTitleStrategy },
    provideHttpClient(withFetch(), withInterceptors([acceptLanguageInterceptor, clientIdInterceptor, authInterceptor, retryInterceptor, traceparentInterceptor])),
    provideStore({
      router: routerReducer,
      [authFeature.name]: authFeature.reducer,
      // Root too: the interceptor and every guard need the current tenant before any route loads.
      [tenantFeature.name]: tenantFeature.reducer,
      // Registered at the root, like auth, instead of on a route: the bell lives in the always-present shell
      // toolbar, so its state must exist on every page for as long as the app does (ADR 0010).
      [notificationsFeature.name]: notificationsFeature.reducer,
    }),
    provideEffects(authEffects, tenantEffects, notificationsEffects, realtimeEffects),
    provideRouterStore(),
    // Development only: in production anyone with the extension could read every action and the state.
    ...(typeof ngDevMode !== 'undefined' && ngDevMode
      ? [provideStoreDevtools({ maxAge: 25, actionSanitizer: hideSecrets, stateSanitizer: hideStateSecrets })]
      : []),
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
