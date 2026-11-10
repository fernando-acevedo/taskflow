import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Subject, of } from 'rxjs';
import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { TenantApiActions } from '@core/tenant/store/tenant.actions';
import { NotificationsApiService, POLL_INTERVAL_MS } from '../notifications-api.service';
import { NotificationBellActions, NotificationsApiActions } from './notifications.actions';
import { notificationsFeature } from './notifications.feature';
import { loadMoreNotifications, loadNotifications, pollUnreadCount } from './notifications.effects';

describe('pollUnreadCount', () => {
  let actions: Subject<Action>;
  let emitted: Action[];
  const unreadCount = vi.fn(() => of({ count: 3 }));

  beforeEach(() => {
    vi.useFakeTimers();
    actions = new Subject<Action>();
    emitted = [];
    unreadCount.mockClear();
    pollUnreadCount(actions as Actions, { unreadCount } as unknown as NotificationsApiService, document).subscribe(
      (action) => emitted.push(action),
    );
  });

  afterEach(() => vi.useRealTimers());

  it('does not poll before anyone is signed in', () => {
    vi.advanceTimersByTime(POLL_INTERVAL_MS * 3);

    expect(unreadCount).not.toHaveBeenCalled();
  });

  it('asks at once and then every 30 seconds after the session is restored', () => {
    actions.next(TenantApiActions.tenantsLoaded({ tenants: [], preferredId: null }));
    vi.advanceTimersByTime(POLL_INTERVAL_MS * 2);

    expect(unreadCount).toHaveBeenCalledTimes(3);
    expect(emitted[0]).toEqual(NotificationsApiActions.unreadCountLoaded({ count: 3 }));
  });

  it('starts after a login and stops on logout', () => {
    actions.next(TenantApiActions.tenantsLoaded({ tenants: [], preferredId: null }));
    vi.advanceTimersByTime(0);
    expect(unreadCount).toHaveBeenCalledTimes(1);

    actions.next(ShellActions.logoutClicked());
    vi.advanceTimersByTime(POLL_INTERVAL_MS * 3);

    expect(unreadCount).toHaveBeenCalledTimes(1);
  });

  it('stops when the session expires', () => {
    actions.next(TenantApiActions.tenantsLoaded({ tenants: [], preferredId: null }));
    actions.next(AuthApiActions.sessionExpired());
    unreadCount.mockClear();
    vi.advanceTimersByTime(POLL_INTERVAL_MS * 3);

    expect(unreadCount).not.toHaveBeenCalled();
  });
});

describe('notification pages', () => {
  const page = { items: [], nextCursor: null };
  const storeWith = (cursor: string | null) =>
    ({ select: (selector: unknown) => of(selector === notificationsFeature.selectNextCursor ? cursor : undefined) }) as unknown as Store;
  const run = (effect: import('rxjs').Observable<Action>) => {
    const out: Action[] = [];
    effect.subscribe((action) => out.push(action));
    return out;
  };

  it('opening the bell loads the first page, without a cursor', () => {
    const list = vi.fn(() => of({ items: [], nextCursor: 'c1' }));
    const out = run(loadNotifications(of(NotificationBellActions.opened()) as Actions, { list } as unknown as NotificationsApiService));

    expect(list).toHaveBeenCalledWith();
    expect(out).toEqual([NotificationsApiActions.notificationsLoaded({ items: [], nextCursor: 'c1', append: false })]);
  });

  it('load more asks for the page after the stored cursor and appends it', () => {
    const list = vi.fn(() => of(page));
    const out = run(
      loadMoreNotifications(of(NotificationBellActions.loadMoreClicked()) as Actions, storeWith('c1'), { list } as unknown as NotificationsApiService),
    );

    expect(list).toHaveBeenCalledWith('c1');
    expect(out).toEqual([NotificationsApiActions.notificationsLoaded({ items: [], nextCursor: null, append: true })]);
  });

  it('load more does nothing on the last page', () => {
    const list = vi.fn(() => of(page));
    const out = run(
      loadMoreNotifications(of(NotificationBellActions.loadMoreClicked()) as Actions, storeWith(null), { list } as unknown as NotificationsApiService),
    );

    expect(out).toEqual([]);
    expect(list).not.toHaveBeenCalled();
  });
});
