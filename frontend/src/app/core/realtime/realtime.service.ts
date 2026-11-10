import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';
import { firstValueFrom } from 'rxjs';
import type { HubConnection, IRetryPolicy } from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { TaskDto, TaskPositionDto } from '../api/types';
import { TokenRefresher } from '../auth/token-refresher';
import { TokenStorage } from '../auth/token-storage';
import { tenantFeature } from '../tenant/store/tenant.feature';
import { clientId } from './client-id';
import { RealtimeActions } from './realtime.actions';

/** The part of SignalR's `HubConnection` the service uses; tests provide a fake. */
export type HubLike = Pick<
  HubConnection,
  'on' | 'start' | 'stop' | 'invoke' | 'onreconnecting' | 'onreconnected' | 'onclose'
>;

export type HubFactory = (url: string, accessToken: () => Promise<string>) => Promise<HubLike>;

/** 1 s, 2 s, 4 s ... up to 30 s (plus jitter), for ever: the project just shows "Reconnecting". */
export function backoffMs(attempt: number): number {
  return Math.min(30_000, 1000 * 2 ** attempt) + Math.floor(Math.random() * 500);
}

const reconnectPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) => backoffMs(previousRetryCount),
};

/** Loads `@microsoft/signalr` on first use, so it is a separate chunk that only opening a project downloads. */
export const HUB_FACTORY = new InjectionToken<HubFactory>('HUB_FACTORY', {
  providedIn: 'root',
  factory: () => async (url, accessTokenFactory) => {
    const { HubConnectionBuilder } = await import('@microsoft/signalr');
    return new HubConnectionBuilder().withUrl(url, { accessTokenFactory }).withAutomaticReconnect(reconnectPolicy).build();
  },
});

/** `disconnected`: the page is open but the connection was cut (the tenant changed under it), so it is not live. */
export type RealtimeState = 'idle' | 'connecting' | 'live' | 'reconnecting' | 'disconnected';

/** A tab hidden for longer than this reloads the open project when it comes back (browsers throttle hidden tabs). */
export const HIDDEN_RELOAD_AFTER_MS = 30_000;

/** The access token is renewed first when it expires within this margin. */
const TOKEN_MARGIN_MS = 60_000;

interface Origin {
  readonly projectId: string;
  readonly originClientId?: string;
}

/**
 * The connection to the project hub (ADR 0023). `join` / `leave` say which projects the open page watches; the
 * connection opens with the first and closes with the last, and `stop` ends it when the session or tenant ends.
 * Events from this tab's own requests are dropped; the rest become `RealtimeActions`.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly store = inject(Store);
  private readonly tokens = inject(TokenStorage);
  private readonly refresher = inject(TokenRefresher);
  private readonly createHub = inject(HUB_FACTORY);
  private readonly document = inject(DOCUMENT);
  private readonly tenantId = this.store.selectSignal(tenantFeature.selectCurrentId);

  private readonly mutableState = signal<RealtimeState>('idle');
  readonly state = this.mutableState.asReadonly();

  private readonly active = new Set<string>();
  /** The tenant the active projects were opened under: a project of another tenant is not joined on a new connection. */
  private activeTenant: string | null = null;
  /** Set when renewing the token failed for good: connecting again would only hit `/hubs` with a dead session. */
  private sessionLost = false;
  private hub: HubLike | null = null;
  /** Changes whenever the connection is stopped, so an attempt that was in flight knows it is obsolete. */
  private generation = 0;
  private connecting = false;
  private hiddenAt: number | null = null;

  constructor() {
    const onVisibility = (): void => {
      if (this.document.visibilityState === 'hidden') {
        this.hiddenAt = Date.now();
        return;
      }
      const hiddenFor = this.hiddenAt === null ? 0 : Date.now() - this.hiddenAt;
      this.hiddenAt = null;
      if (hiddenFor > HIDDEN_RELOAD_AFTER_MS) {
        this.resyncAll();
      }
    };
    this.document.addEventListener('visibilitychange', onVisibility);
    inject(DestroyRef).onDestroy(() => this.document.removeEventListener('visibilitychange', onVisibility));
  }

  join(projectId: string): void {
    if (this.active.size === 0) {
      this.activeTenant = this.tenantId();
    }
    this.active.add(projectId);
    if (this.hub !== null && this.mutableState() === 'live') {
      void this.invoke('JoinProject', projectId);
    } else if (!this.connecting && this.hub === null) {
      void this.connect();
    }
  }

  leave(projectId: string): void {
    if (!this.active.delete(projectId)) {
      return;
    }
    if (this.active.size === 0) {
      this.stop();
    } else if (this.hub !== null && this.mutableState() === 'live') {
      void this.invoke('LeaveProject', projectId);
    }
  }

  /** Closes the connection and forgets the projects: the session ended. */
  stop(): void {
    this.suspend();
    this.active.clear();
    this.activeTenant = null;
    this.mutableState.set('idle');
  }

  /**
   * Closes the connection but keeps the projects the open page watches: the tenant changed, and if the page stays
   * (a guard blocked the navigation home) it must show "Disconnected" rather than a stale "Live". The page's own
   * `leave` ends the watch; a project of the old tenant is never joined on a connection of the new one.
   */
  suspend(): void {
    this.generation++;
    this.connecting = false;
    const hub = this.hub;
    this.hub = null;
    this.mutableState.set(this.active.size > 0 ? 'disconnected' : 'idle');
    hub?.stop().catch(() => undefined);
  }

  private async connect(): Promise<void> {
    const generation = this.generation;
    const tenant = this.tenantId();
    if (tenant === null || this.active.size === 0) {
      return;
    }
    if (tenant !== this.activeTenant) {
      this.mutableState.set('disconnected');
      return;
    }
    this.sessionLost = false;
    this.connecting = true;
    this.mutableState.set('connecting');
    const url = `${environment.apiUrl}/hubs/projects?tenant=${encodeURIComponent(tenant)}`;
    let failures = 0;
    while (generation === this.generation) {
      let hub: HubLike | null = null;
      try {
        hub = await this.createHub(url, () => this.accessToken());
        if (generation !== this.generation) {
          return;
        }
        this.listen(hub, generation);
        await hub.start();
        if (generation !== this.generation) {
          hub.stop().catch(() => undefined);
          return;
        }
        this.hub = hub;
        this.connecting = false;
        await this.rejoin();
        // A connection that needed retries may have missed events.
        if (failures > 0) {
          this.resyncAll();
        }
        return;
      } catch {
        hub?.stop().catch(() => undefined);
        if (this.sessionLost && generation === this.generation) {
          this.stop();
          return;
        }
        failures++;
        this.mutableState.set('reconnecting');
        await new Promise((resolve) => setTimeout(resolve, backoffMs(failures - 1)));
      }
    }
  }

  private listen(hub: HubLike, generation: number): void {
    const foreign = (event: Origin): boolean =>
      generation === this.generation && event.originClientId !== clientId() && this.active.has(event.projectId);

    hub.on('taskChanged', (event: Origin & { task: TaskDto }) => {
      if (foreign(event)) {
        this.store.dispatch(RealtimeActions.taskChanged({ projectId: event.projectId, task: event.task }));
      }
    });
    hub.on('taskDeleted', (event: Origin & { taskId: string }) => {
      if (foreign(event)) {
        this.store.dispatch(RealtimeActions.taskDeleted({ projectId: event.projectId, taskId: event.taskId }));
      }
    });
    hub.on('tasksReordered', (event: Origin & { tasks: TaskPositionDto[] }) => {
      if (foreign(event)) {
        this.store.dispatch(RealtimeActions.tasksReordered({ projectId: event.projectId, tasks: event.tasks }));
      }
    });
    hub.on('commentsChanged', (event: Origin & { taskId: string }) => {
      if (foreign(event)) {
        this.store.dispatch(RealtimeActions.commentsChanged({ projectId: event.projectId, taskId: event.taskId }));
      }
    });
    hub.onreconnecting(() => {
      if (generation === this.generation) {
        this.mutableState.set('reconnecting');
      }
    });
    hub.onreconnected(() => {
      if (generation === this.generation) {
        void this.rejoin().then(() => this.resyncAll());
      }
    });
    hub.onclose(() => {
      // Only reached when the server closed for good (the automatic reconnect never gives up): start over.
      if (generation === this.generation && this.hub === hub) {
        this.hub = null;
        this.mutableState.set('reconnecting');
        void this.connect();
      }
    });
  }

  private async rejoin(): Promise<void> {
    this.mutableState.set('live');
    await Promise.all([...this.active].map((projectId) => this.invoke('JoinProject', projectId)));
  }

  private async invoke(method: string, projectId: string): Promise<void> {
    try {
      await this.hub?.invoke(method, projectId);
    } catch {
      // The server refused (no access) or the call was cut off; the reload on return keeps the page current.
    }
  }

  private resyncAll(): void {
    for (const projectId of this.active) {
      this.store.dispatch(RealtimeActions.resyncRequested({ projectId }));
    }
  }

  /** The current access token; one about to expire is renewed first, with the refresh the interceptor already uses. */
  private async accessToken(): Promise<string> {
    if (this.tokens.accessTokenExpiresWithin(TOKEN_MARGIN_MS)) {
      if (this.tokens.refreshToken()) {
        try {
          return await firstValueFrom(this.refresher.refresh());
        } catch (error) {
          // The refresh was refused (401) or is impossible: retrying would only repeat it.
          this.sessionLost = true;
          throw error;
        }
      }
      if (this.tokens.accessTokenExpiresWithin(0)) {
        this.sessionLost = true;
        throw new Error('The session has expired.');
      }
    }
    return this.tokens.accessToken() ?? '';
  }
}
