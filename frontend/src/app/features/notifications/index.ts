/** Public API of the notifications feature: what other features may import. */
export * as notificationsEffects from './data-access/store/notifications.effects';
export { notificationsFeature } from './data-access/store/notifications.feature';
export { NotificationBell } from './ui/notification-bell';
/**
 * The preferences card is loaded on demand: this barrel is imported by the app shell, and a static export would
 * put the card (and its slide toggles) in the initial bundle.
 */
export const loadNotificationPreferencesCard = () => import('./ui/notification-preferences').then((m) => m.NotificationPreferencesCard);
