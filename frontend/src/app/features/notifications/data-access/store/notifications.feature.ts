import { createFeature, createReducer, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { TenantActions } from '../../../../core/tenant/store/tenant.actions';
import { ApiError } from '../../../../core/http/api-error';
import { AppNotification, NotificationPreferences } from '../notifications-api.service';
import {
  NotificationBellActions,
  NotificationPreferencesActions,
  NotificationsApiActions,
} from './notifications.actions';

export interface NotificationsState {
  unreadCount: number;
  items: readonly AppNotification[];
  loading: boolean;
  loadError: ApiError | null;
  preferences: NotificationPreferences | null;
  preferencesLoading: boolean;
  preferencesError: ApiError | null;
  preferencesSaving: boolean;
}

export const initialNotificationsState: NotificationsState = {
  unreadCount: 0,
  items: [],
  loading: false,
  loadError: null,
  preferences: null,
  preferencesLoading: false,
  preferencesError: null,
  preferencesSaving: false,
};

export const notificationsFeature = createFeature({
  name: 'notifications',
  reducer: createReducer(
    initialNotificationsState,
    // Another user may sign in next in this tab; nothing of the previous session may show.
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, TenantActions.tenantSwitched, (): NotificationsState => initialNotificationsState),
    on(NotificationsApiActions.unreadCountLoaded, (state, { count }): NotificationsState => ({
      ...state,
      unreadCount: count,
    })),
    on(NotificationBellActions.opened, (state): NotificationsState => ({
      ...state,
      loading: true,
      loadError: null,
    })),
    on(NotificationsApiActions.notificationsLoaded, (state, { items }): NotificationsState => ({
      ...state,
      items,
      loading: false,
    })),
    on(NotificationsApiActions.notificationsLoadFailed, (state, { error }): NotificationsState => ({
      ...state,
      loading: false,
      loadError: error,
    })),
    // Optimistic: the API call follows, and a failure reloads the truth.
    on(NotificationBellActions.notificationClicked, (state, { id, at }): NotificationsState => {
      const target = state.items.find((item) => item.id === id);
      if (target?.readAt !== null) {
        return state;
      }
      return {
        ...state,
        unreadCount: Math.max(0, state.unreadCount - 1),
        items: state.items.map((item) => (item.id === id ? { ...item, readAt: at } : item)),
      };
    }),
    on(NotificationBellActions.markAllClicked, (state, { at }): NotificationsState => ({
      ...state,
      unreadCount: 0,
      items: state.items.map((item) => ({ ...item, readAt: item.readAt ?? at })),
    })),
    on(NotificationPreferencesActions.opened, (state): NotificationsState => ({
      ...state,
      preferencesLoading: true,
      preferencesError: null,
    })),
    on(NotificationsApiActions.preferencesLoaded, (state, { preferences }): NotificationsState => ({
      ...state,
      preferences,
      preferencesLoading: false,
    })),
    on(NotificationsApiActions.preferencesLoadFailed, (state, { error }): NotificationsState => ({
      ...state,
      preferencesLoading: false,
      preferencesError: error,
    })),
    on(NotificationPreferencesActions.saveSubmitted, (state): NotificationsState => ({ ...state, preferencesSaving: true })),
    on(NotificationsApiActions.preferencesSaved, (state, { preferences }): NotificationsState => ({
      ...state,
      preferences,
      preferencesSaving: false,
    })),
    on(NotificationsApiActions.preferencesSaveFailed, (state): NotificationsState => ({ ...state, preferencesSaving: false })),
  ),
});
