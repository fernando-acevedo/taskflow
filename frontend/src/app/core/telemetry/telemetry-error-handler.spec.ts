import { TestBed } from '@angular/core/testing';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { authFeature } from '../auth/store/auth.feature';
import { tenantFeature } from '../tenant/store/tenant.feature';
import {
  APP_INSIGHTS_CLIENT_FACTORY,
  APP_INSIGHTS_CONNECTION_STRING,
  TelemetryClient,
  TelemetryErrorHandler,
  TelemetryItem,
  scrub,
  stripUrl,
  stripUrlSecrets,
} from './telemetry-error-handler';

function setup(connectionString: string) {
  const client: TelemetryClient = { trackException: vi.fn(), setAuthenticatedUserContext: vi.fn() };
  const factory = vi.fn().mockResolvedValue(client);
  TestBed.configureTestingModule({
    providers: [
      TelemetryErrorHandler,
      { provide: APP_INSIGHTS_CONNECTION_STRING, useValue: connectionString },
      { provide: APP_INSIGHTS_CLIENT_FACTORY, useValue: factory },
      provideMockStore({
        selectors: [
          { selector: authFeature.selectUser, value: { id: 'user-1', email: 'ana@example.com', displayName: 'Ana', roles: [] } },
          { selector: tenantFeature.selectCurrentId, value: 'tenant-1' },
        ],
      }),
    ],
  });
  TestBed.inject(MockStore);
  return { handler: TestBed.inject(TelemetryErrorHandler), client, factory };
}

describe('TelemetryErrorHandler', () => {
  beforeEach(() => vi.spyOn(console, 'error').mockImplementation(() => undefined));
  afterEach(() => vi.restoreAllMocks());

  it('only logs to the console, and loads nothing, without a connection string', () => {
    const { handler, factory } = setup('');
    const error = new Error('boom');

    handler.handleError(error);

    expect(console.error).toHaveBeenCalledWith(error);
    expect(factory).not.toHaveBeenCalled();
  });

  it('also reports the exception with the user id and tenant, but no email or token, when configured', async () => {
    const { handler, client, factory } = setup('InstrumentationKey=abc');

    handler.handleError(new Error('failed for ana@example.com with Bearer abc.def.ghi'));
    await vi.waitFor(() => expect(client.trackException).toHaveBeenCalled());

    expect(console.error).toHaveBeenCalled();
    expect(factory).toHaveBeenCalledWith('InstrumentationKey=abc');
    expect(client.setAuthenticatedUserContext).toHaveBeenCalledWith('user-1', undefined, false);
    const sent = vi.mocked(client.trackException).mock.calls[0][0];
    expect(sent.properties).toEqual({ tenantId: 'tenant-1' });
    expect(sent.exception.message).toBe('failed for [redacted-email] with Bearer [redacted]');
  });

  it('loads the SDK once for several errors', async () => {
    const { handler, client, factory } = setup('InstrumentationKey=abc');

    handler.handleError(new Error('a'));
    handler.handleError(new Error('b'));
    await vi.waitFor(() => expect(client.trackException).toHaveBeenCalledTimes(2));

    expect(factory).toHaveBeenCalledTimes(1);
  });
});

describe('scrub', () => {
  it('hides JWTs', () => {
    expect(scrub('t=eyJhbGciOi.eyJzdWIi.sig-1')).toBe('t=[redacted-token]');
  });
});

describe('stripUrlSecrets', () => {
  const page = 'https://app.example.com/accept-invite?email=a@b.c#token=SECRET&userId=1';

  it('removes the fragment and the query from the URL fields before an item is sent', () => {
    const item: TelemetryItem = {
      baseData: { uri: page, refUri: page, name: 'x', nested: { pageUrl: page } },
      ext: { page: { pageUrl: page } },
      tags: { 'ai.operation.name': '/accept-invite', 'ai.location.url': page },
    };

    expect(stripUrlSecrets(item)).toBe(true);

    expect(item.baseData).toEqual({ uri: 'https://app.example.com/accept-invite', refUri: 'https://app.example.com/accept-invite', name: 'x', nested: { pageUrl: 'https://app.example.com/accept-invite' } });
    expect(item.ext).toEqual({ page: { pageUrl: 'https://app.example.com/accept-invite' } });
    expect(item.tags).toEqual({ 'ai.operation.name': '/accept-invite', 'ai.location.url': 'https://app.example.com/accept-invite' });
    expect(JSON.stringify(item)).not.toContain('SECRET');
  });

  it('keeps a URL that has neither, and tolerates items without parts', () => {
    expect(stripUrl('https://x.test/a/b')).toBe('https://x.test/a/b');
    expect(stripUrlSecrets({})).toBe(true);
  });

  it('is registered on the real SDK client when it is created', async () => {
    const added: unknown[] = [];
    vi.doMock('@microsoft/applicationinsights-web', () => ({
      ApplicationInsights: class {
        loadAppInsights(): void {
          // nothing to load in the test
        }
        addTelemetryInitializer(initializer: unknown): void {
          added.push(initializer);
        }
      },
    }));
    const { APP_INSIGHTS_CLIENT_FACTORY: token } = await import('./telemetry-error-handler');
    const factory = TestBed.inject(token);

    await factory('InstrumentationKey=k');

    expect(added).toEqual([stripUrlSecrets]);
    vi.doUnmock('@microsoft/applicationinsights-web');
  });
});
