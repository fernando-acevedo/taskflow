import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { AppNotification } from '../data-access/notifications-api.service';
import { NotificationBellActions } from '../data-access/store/notifications.actions';
import { notificationsFeature } from '../data-access/store/notifications.feature';
import { NotificationBell } from './notification-bell';

const unread: AppNotification = {
  id: 'n1',
  type: 'Mentioned',
  projectId: 'p1',
  taskId: 't1',
  actorId: 'u2',
  data: { taskTitle: 'Brief', projectName: 'Site', actorName: 'Grace' },
  createdAt: new Date().toISOString(),
  readAt: null,
};
const read: AppNotification = {
  ...unread,
  id: 'n2',
  type: 'TaskDueSoon',
  readAt: new Date().toISOString(),
  data: { taskTitle: 'Launch', projectName: 'Site', dueDate: '2026-11-07' },
};

describe('NotificationBell', () => {
  let store: MockStore;
  let router: Router;

  function render(count: number, items: AppNotification[], hasMore = false): HTMLElement {
    TestBed.configureTestingModule({
      imports: [
        NotificationBell,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideRouter([]),
        provideMockStore({
          selectors: [
            { selector: notificationsFeature.selectUnreadCount, value: count },
            { selector: notificationsFeature.selectItems, value: items },
            { selector: notificationsFeature.selectHasMore, value: hasMore },
            { selector: notificationsFeature.selectLoading, value: false },
            { selector: notificationsFeature.selectLoadError, value: null },
          ],
        }),
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    router = TestBed.inject(Router);
    vi.spyOn(store, 'dispatch');
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(NotificationBell);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function openMenu(element: HTMLElement): HTMLElement[] {
    element.querySelector<HTMLButtonElement>('button.bell')?.click();
    TestBed.tick();
    return [...document.querySelectorAll<HTMLElement>('.mat-mdc-menu-panel .item')];
  }

  it('shows the unread count on the bell', () => {
    expect(render(3, []).querySelector('.mat-badge-content')?.textContent).toContain('3');
  });

  it('hides the badge when nothing is unread', () => {
    expect(render(0, []).querySelector('.mat-badge-hidden')).not.toBeNull();
  });

  it('reloads the list when the menu opens and shows an empty state when there is none', () => {
    const element = render(0, []);

    openMenu(element);

    expect(store.dispatch).toHaveBeenCalledWith(NotificationBellActions.opened());
    expect(document.querySelector('.mat-mdc-menu-panel app-empty-state')).not.toBeNull();
  });

  it('styles unread items and, on click, marks one read and opens its task on the board', () => {
    const element = render(1, [unread, read]);

    const items = openMenu(element);
    expect(items.map((item) => item.classList.contains('unread'))).toEqual([true, false]);
    items[0].click();

    expect(store.dispatch).toHaveBeenCalledWith(
      expect.objectContaining({ type: NotificationBellActions.notificationClicked.type, id: 'n1' }),
    );
    expect(router.navigate).toHaveBeenCalledWith(['/projects', 'p1', 'board'], { queryParams: { task: 't1' } });
  });

  it('offers "load more" only while there is a next page, and asks for it', () => {
    openMenu(render(1, [unread]));
    expect(document.querySelector('.mat-mdc-menu-panel .more button')).toBeNull();
    TestBed.resetTestingModule();
    document.body.replaceChildren();

    openMenu(render(1, [unread], true));
    document.querySelector<HTMLButtonElement>('.mat-mdc-menu-panel .more button')?.click();

    expect(store.dispatch).toHaveBeenCalledWith(NotificationBellActions.loadMoreClicked());
  });

  it('marks all as read from the menu header', () => {
    const element = render(1, [unread]);

    openMenu(element);
    document.querySelector<HTMLButtonElement>('.mat-mdc-menu-panel .menu-header button')?.click();

    expect(store.dispatch).toHaveBeenCalledWith(expect.objectContaining({ type: NotificationBellActions.markAllClicked.type }));
  });
});
