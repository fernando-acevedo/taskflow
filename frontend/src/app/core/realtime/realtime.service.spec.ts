import { TestBed } from '@angular/core/testing';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of, throwError } from 'rxjs';
import { TokenRefresher } from '../auth/token-refresher';
import { TokenStorage } from '../auth/token-storage';
import { clientId } from './client-id';
import { RealtimeActions } from './realtime.actions';
import { HIDDEN_RELOAD_AFTER_MS, HUB_FACTORY, HubLike, RealtimeService } from './realtime.service';

/** A hub that records what the service does and lets the test raise server events, without any network. */
class FakeHub {
  readonly handlers = new Map<string, (event: unknown) => void>();
  readonly invoked: [string, string][] = [];
  reconnecting: (() => void) | null = null;
  reconnected: (() => void) | null = null;
  started = 0;
  stopped = 0;
  startFails = 0;
  /** Like SignalR, which asks for the access token while starting. */
  beforeStart: (() => Promise<unknown>) | null = null;

  on = (name: string, handler: (event: never) => void): void => void this.handlers.set(name, handler as (event: unknown) => void);
  onreconnecting = (handler: () => void): void => void (this.reconnecting = handler);
  onreconnected = (handler: () => void): void => void (this.reconnected = handler);
  onclose = (): void => undefined;
  start = async (): Promise<void> => {
    this.started++;
    await this.beforeStart?.();
    if (this.startFails-- > 0) {
      throw new Error('down');
    }
  };
  stop = (): Promise<void> => {
    this.stopped++;
    return Promise.resolve();
  };
  invoke = (method: string, projectId: string): Promise<void> => {
    this.invoked.push([method, projectId]);
    return Promise.resolve();
  };
  emit(name: string, event: unknown): void {
    this.handlers.get(name)?.(event);
  }
}

const settle = async (): Promise<void> => {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
};

describe('RealtimeService', () => {
  let hub: FakeHub;
  let urls: string[];
  let store: MockStore;
  let service: RealtimeService;
  let visibility: DocumentVisibilityState;
  let tokenFactory: () => Promise<string>;

  beforeEach(() => {
    hub = new FakeHub();
    urls = [];
    visibility = 'visible';
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
    TestBed.configureTestingModule({
      providers: [
        provideMockStore({ initialState: { tenant: { tenants: [], currentId: 't1', status: 'ready' } } }),
        {
          provide: HUB_FACTORY,
          useValue: (url: string, accessToken: () => Promise<string>) => {
            urls.push(url);
            tokenFactory = accessToken;
            return Promise.resolve(hub as unknown as HubLike);
          },
        },
        { provide: TokenStorage, useValue: { accessToken: () => 'tok', refreshToken: () => 'r', accessTokenExpiresWithin: () => false } },
        { provide: TokenRefresher, useValue: { refresh: () => of('fresh') } },
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');
    service = TestBed.inject(RealtimeService);
  });

  afterEach(() => vi.restoreAllMocks());

  async function joined(): Promise<void> {
    service.join('p1');
    await settle();
  }

  it('connects with the tenant in the query on the first join and joins the project', async () => {
    await joined();

    expect(urls).toHaveLength(1);
    expect(urls[0]).toContain('/hubs/projects?tenant=t1');
    expect(hub.invoked).toEqual([['JoinProject', 'p1']]);
    expect(service.state()).toBe('live');
  });

  it('turns the events of other tabs into actions and ignores its own', async () => {
    await joined();
    const task = { id: 'a' };

    hub.emit('taskChanged', { projectId: 'p1', task, originClientId: clientId() });
    hub.emit('taskChanged', { projectId: 'p1', task, originClientId: 'someone-else' });
    hub.emit('taskDeleted', { projectId: 'p1', taskId: 'a', originClientId: clientId() });
    hub.emit('taskDeleted', { projectId: 'p1', taskId: 'a' });
    hub.emit('tasksReordered', { projectId: 'p1', tasks: [], originClientId: 'x' });
    hub.emit('commentsChanged', { projectId: 'p1', taskId: 'a', originClientId: 'x' });
    hub.emit('taskChanged', { projectId: 'other', task, originClientId: 'x' });

    expect(store.dispatch).toHaveBeenCalledTimes(4);
    expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.taskChanged({ projectId: 'p1', task: task as never }));
    expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.taskDeleted({ projectId: 'p1', taskId: 'a' }));
    expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.tasksReordered({ projectId: 'p1', tasks: [] }));
    expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.commentsChanged({ projectId: 'p1', taskId: 'a' }));
  });

  it('shows reconnecting, then rejoins the active projects and reloads them when the connection returns', async () => {
    await joined();
    hub.invoked.length = 0;

    hub.reconnecting?.();
    expect(service.state()).toBe('reconnecting');
    hub.reconnected?.();
    await settle();

    expect(service.state()).toBe('live');
    expect(hub.invoked).toEqual([['JoinProject', 'p1']]);
    expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.resyncRequested({ projectId: 'p1' }));
  });

  it('retries a failed first connection and reloads once it is up', async () => {
    vi.useFakeTimers();
    try {
      hub.startFails = 1;
      service.join('p1');
      await vi.advanceTimersByTimeAsync(2000);

      expect(hub.started).toBe(2);
      expect(service.state()).toBe('live');
      expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.resyncRequested({ projectId: 'p1' }));
    } finally {
      vi.useRealTimers();
    }
  });

  it('leaves the project and closes the connection with the last one', async () => {
    await joined();
    service.join('p2');
    await settle();

    service.leave('p2');
    expect(hub.invoked).toContainEqual(['LeaveProject', 'p2']);
    expect(hub.stopped).toBe(0);

    service.leave('p1');
    expect(hub.stopped).toBe(1);
    expect(service.state()).toBe('idle');
  });

  it('stop cuts the connection and drops later events', async () => {
    await joined();

    service.stop();
    hub.emit('taskDeleted', { projectId: 'p1', taskId: 'a', originClientId: 'x' });

    expect(hub.stopped).toBe(1);
    expect(service.state()).toBe('idle');
    expect(store.dispatch).not.toHaveBeenCalled();
  });

  describe('when the tenant changes under an open page', () => {
    it('suspends: closes the hub but keeps the project and shows disconnected', async () => {
      await joined();

      service.suspend();

      expect(hub.stopped).toBe(1);
      expect(service.state()).toBe('disconnected');
      hub.emit('taskDeleted', { projectId: 'p1', taskId: 'a', originClientId: 'x' });
      expect(store.dispatch).not.toHaveBeenCalled();
    });

    it('does not join a project of the old tenant on a connection of the new one', async () => {
      await joined();
      service.suspend();
      urls.length = 0;
      hub.invoked.length = 0;
      store.setState({ tenant: { tenants: [], currentId: 't2', status: 'ready' } });

      service.join('p1');
      await settle();

      expect(urls).toHaveLength(0);
      expect(hub.invoked).toEqual([]);
      expect(service.state()).toBe('disconnected');
    });

    it('goes idle when the page leaves after a suspend', async () => {
      await joined();
      service.suspend();

      service.leave('p1');

      expect(service.state()).toBe('idle');
    });
  });

  describe('when the session cannot be renewed', () => {
    it('gives up instead of retrying against the hub', async () => {
      const tokens = TestBed.inject(TokenStorage) as unknown as { accessTokenExpiresWithin: () => boolean };
      tokens.accessTokenExpiresWithin = () => true;
      const refresher = TestBed.inject(TokenRefresher) as unknown as { refresh: () => unknown };
      refresher.refresh = () => throwError(() => new Error('401'));
      hub.beforeStart = () => tokenFactory();

      service.join('p1');
      await settle();

      expect(urls).toHaveLength(1);
      expect(hub.started).toBe(1);
      expect(service.state()).toBe('idle');
    });
  });

  describe('when the tab becomes visible again', () => {
    const hideFor = (ms: number): void => {
      vi.useFakeTimers();
      visibility = 'hidden';
      document.dispatchEvent(new Event('visibilitychange'));
      vi.advanceTimersByTime(ms);
      visibility = 'visible';
      document.dispatchEvent(new Event('visibilitychange'));
      vi.useRealTimers();
    };

    it('reloads the open project after a long absence', async () => {
      await joined();

      hideFor(HIDDEN_RELOAD_AFTER_MS + 1000);

      expect(store.dispatch).toHaveBeenCalledWith(RealtimeActions.resyncRequested({ projectId: 'p1' }));
    });

    it('does nothing after a short one', async () => {
      await joined();

      hideFor(2000);

      expect(store.dispatch).not.toHaveBeenCalled();
    });
  });
});
