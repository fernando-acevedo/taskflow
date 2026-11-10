/** Public API of the activity feature: what other features may import. */
export { activityFeature } from './data-access/store/activity.feature';
export { TaskHistory } from './ui/task-history';
export * as activityEffects from './data-access/store/activity.effects';
/** Loaded on demand, so the page is its own chunk (the barrel is imported by the project routes). */
export const loadActivityPage = () => import('./pages/activity-page').then((m) => m.ActivityPage);
