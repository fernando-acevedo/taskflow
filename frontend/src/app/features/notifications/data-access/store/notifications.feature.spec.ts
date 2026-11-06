import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { AppNotification } from '../notifications-api.service';
import { NotificationBellActions, NotificationsApiActions } from './notifications.actions';
import { initialNotificationsState, notificationsFeature } from './notifications.feature';

function aNotification(id: string, readAt: string | null = null): AppNotification {
  return {
    id,
    type: 'TaskAssigned',
    projectId: 'p1',
    taskId: 't1',
    actorId: 'u2',
    data: { taskTitle: 'Brief', projectName: 'Site', actorName: 'Grace' },
    createdAt: '2026-11-05T10:00:00Z',
    readAt,
  };
}

describe('notificationsFeature', () => {
  const { reducer } = notificationsFeature;
  const loaded = reducer(
    reducer(initialNotificationsState, NotificationsApiActions.unreadCountLoaded({ count: 2 })),
    NotificationsApiActions.notificationsLoaded({
      items: [aNotification('a'), aNotification('b'), aNotification('c', '2026-11-05T11:00:00Z')],
    }),
  );

  it('shows a clicked notification as read at once and lowers the count', () => {
    const state = reducer(loaded, NotificationBellActions.notificationClicked({ id: 'a', at: '2026-11-06T09:00:00Z' }));

    expect(state.unreadCount).toBe(1);
    expect(state.items.find((item) => item.id === 'a')?.readAt).toBe('2026-11-06T09:00:00Z');
  });

  it('does not lower the count for a notification that was already read', () => {
    const state = reducer(loaded, NotificationBellActions.notificationClicked({ id: 'c', at: '2026-11-06T09:00:00Z' }));

    expect(state).toBe(loaded);
  });

  it('marks everything as read, keeping the earlier read times', () => {
    const state = reducer(loaded, NotificationBellActions.markAllClicked({ at: '2026-11-06T09:00:00Z' }));

    expect(state.unreadCount).toBe(0);
    expect(state.items.map((item) => item.readAt)).toEqual([
      '2026-11-06T09:00:00Z',
      '2026-11-06T09:00:00Z',
      '2026-11-05T11:00:00Z',
    ]);
  });

  it.each([ShellActions.logoutClicked(), AuthApiActions.sessionExpired()])('forgets everything on $type', (action) => {
    expect(reducer(loaded, action)).toEqual(initialNotificationsState);
  });
});
