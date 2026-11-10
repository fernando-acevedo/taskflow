/** Public API of the dashboard feature: what other features may import. */
export { BarList } from './ui/bar-list';
export type { BarRow } from './ui/bar-list';
export * as projectDashboardEffects from './data-access/store/project-dashboard.effects';
export { projectDashboardFeature } from './data-access/store/project-dashboard.feature';
export const loadProjectOverviewPage = () => import('./pages/project-overview-page').then((m) => m.ProjectOverviewPage);
/** The home page's routes. App routes load this barrel on demand, so `BarList` stays out of the initial bundle. */
export const loadDashboardRoutes = () => import('./dashboard.routes').then((m) => m.dashboardRoutes);
