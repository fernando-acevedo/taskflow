import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError } from '@shared/data-access/api-error';
import { AppNotification, NotificationPreferences } from '../notifications-api.service';

export const NotificationBellActions = createActionGroup({
  source: 'Notification Bell',
  events: {
    Opened: emptyProps(),
    'Load More Clicked': emptyProps(),
    /** `at` is the time to show as "read" until the list reloads. */
    'Notification Clicked': props<{ id: string; at: string }>(),
    'Mark All Clicked': props<{ at: string }>(),
  },
});

export const NotificationPreferencesActions = createActionGroup({
  source: 'Notification Preferences',
  events: {
    Opened: emptyProps(),
    'Save Submitted': props<{ preferences: NotificationPreferences }>(),
  },
});

export const NotificationsApiActions = createActionGroup({
  source: 'Notifications API',
  events: {
    'Unread Count Loaded': props<{ count: number }>(),
    /** `append` is false for the first page (it replaces the list) and true for "load more". */
    /** `append` is false for the first page (it replaces the list) and true for "load more". */
    'Notifications Loaded': props<{ items: readonly AppNotification[]; nextCursor: string | null; append: boolean }>(),
    'Notifications Load Failed': props<{ error: ApiError }>(),
    'Mark Failed': props<{ error: ApiError }>(),
    'Preferences Loaded': props<{ preferences: NotificationPreferences }>(),
    'Preferences Load Failed': props<{ error: ApiError }>(),
    'Preferences Saved': props<{ preferences: NotificationPreferences }>(),
    'Preferences Save Failed': props<{ error: ApiError }>(),
  },
});
