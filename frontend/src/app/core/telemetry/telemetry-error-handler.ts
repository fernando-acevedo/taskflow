import { ErrorHandler, Injectable, InjectionToken, Injector, inject } from '@angular/core';
import { Store } from '@ngrx/store';
import { environment } from '../../../environments/environment';
import { authFeature } from '../auth/store/auth.feature';
import { tenantFeature } from '../tenant/store/tenant.feature';

/** The slice of the Application Insights SDK that the handler uses. */
export interface TelemetryClient {
  trackException(exception: { exception: Error; properties?: Record<string, string> }): void;
  setAuthenticatedUserContext(authenticatedUserId: string, accountId?: string, storeInCookie?: boolean): void;
}

export type TelemetryClientFactory = (connectionString: string) => Promise<TelemetryClient>;

/** What the SDK hands to a telemetry initializer: the part of an item this module reads and edits. */
export interface TelemetryItem {
  baseData?: Record<string, unknown>;
  ext?: Record<string, unknown>;
  data?: Record<string, unknown>;
  tags?: Record<string, unknown>;
}

/** Empty by default: nothing is loaded or sent unless a deployment sets `appInsightsConnectionString`. */
export const APP_INSIGHTS_CONNECTION_STRING = new InjectionToken<string>('APP_INSIGHTS_CONNECTION_STRING', {
  factory: () => environment.appInsightsConnectionString,
});

/**
 * Loads the SDK with a dynamic import, so it is its own lazy chunk that is only downloaded when a connection
 * string is set (and an error occurs). Only exceptions are reported: the SDK's own page-view, route, ajax/fetch
 * and exception auto-collection are off. Its correlation headers are off too, because `traceparentInterceptor`
 * already sends the W3C `traceparent` for every API call (two different ones would split the trace).
 */
/** The slice of the SDK used to register the initializer. */
interface InitializableClient {
  addTelemetryInitializer(initializer: (item: TelemetryItem) => boolean): void;
}

export const APP_INSIGHTS_CLIENT_FACTORY = new InjectionToken<TelemetryClientFactory>('APP_INSIGHTS_CLIENT_FACTORY', {
  factory: () => async (connectionString) => {
    const { ApplicationInsights } = await import('@microsoft/applicationinsights-web');
    const insights = new ApplicationInsights({
      config: {
        connectionString,
        disableCorrelationHeaders: true,
        disableAjaxTracking: true,
        disableFetchTracking: true,
        disableExceptionTracking: true,
        autoTrackPageVisitTime: false,
        enableAutoRouteTracking: false,
        isCookieUseDisabled: true,
      },
    });
    insights.loadAppInsights();
    (insights as InitializableClient).addTelemetryInitializer(stripUrlSecrets);
    return insights;
  },
});

/** A URL without its fragment and query: emailed links carry their tokens there. Text that isn't a URL is only scrubbed. */
export function stripUrl(value: string): string {
  return value.replace(/[?#].*$/s, '');
}

const URL_FIELD = /ur[il]/i;

function stripUrlFields(node: unknown, depth = 0): void {
  if (node === null || typeof node !== 'object' || depth > 6) {
    return;
  }
  const record = node as Record<string, unknown>;
  for (const key of Object.keys(record)) {
    const value = record[key];
    if (typeof value === 'string' && URL_FIELD.test(key)) {
      record[key] = stripUrl(value);
    } else {
      stripUrlFields(value, depth + 1);
    }
  }
}

/**
 * Telemetry initializer: the SDK adds the page's URL to every item (`baseData.uri`, `refUri`, and fields of
 * the `ext`/`data`/`tags` parts), and the links in emails carry tokens in the fragment. Removes the fragment and
 * the query from every field that holds a URL before the item is sent. Always lets the item through.
 */
export function stripUrlSecrets(item: TelemetryItem): boolean {
  stripUrlFields(item.baseData);
  stripUrlFields(item.ext);
  stripUrlFields(item.data);
  stripUrlFields(item.tags);
  return true;
}

/** Removes what must never leave the browser from a message: bearer/JWT tokens and email addresses. */
export function scrub(text: string): string {
  return text
    .replace(/Bearer\s+[\w.~+/=-]+/gi, 'Bearer [redacted]')
    .replace(/eyJ[\w-]+\.[\w-]+\.[\w-]+/g, '[redacted-token]')
    .replace(/[^\s@"'<>]+@[^\s@"'<>]+\.[^\s@"'<>]+/g, '[redacted-email]');
}

function toError(error: unknown): Error {
  const source = error instanceof Error ? error : new Error(typeof error === 'string' ? error : 'Unhandled error');
  const clean = new Error(scrub(source.message));
  clean.name = source.name;
  clean.stack = source.stack ? scrub(source.stack) : undefined;
  return clean;
}

/**
 * Replaces Angular's ErrorHandler: always logs to the console and, when an Application Insights connection
 * string is configured, also reports the exception. Reports carry the user id and tenant id, never the email or
 * any token.
 */
@Injectable()
export class TelemetryErrorHandler implements ErrorHandler {
  private readonly connectionString = inject(APP_INSIGHTS_CONNECTION_STRING);
  private readonly createClient = inject(APP_INSIGHTS_CLIENT_FACTORY);
  // Looked up when an error is reported, not injected: with the NgRx devtools on, the Store depends on this
  // ErrorHandler, so injecting the Store here would be a circular dependency.
  private readonly injector = inject(Injector);
  private client: Promise<TelemetryClient> | null = null;

  handleError(error: unknown): void {
    console.error(error);

    if (!this.connectionString) {
      return;
    }

    const store = this.injector.get(Store);
    const userId = store.selectSignal(authFeature.selectUser)()?.id;
    const tenantId = store.selectSignal(tenantFeature.selectCurrentId)();
    this.client ??= this.createClient(this.connectionString);
    this.client
      .then((client) => {
        if (userId) {
          client.setAuthenticatedUserContext(userId, undefined, false);
        }
        client.trackException({ exception: toError(error), properties: tenantId ? { tenantId } : {} });
      })
      .catch(() => {
        // Telemetry must never break the app; retry loading on the next error.
        this.client = null;
      });
  }
}
