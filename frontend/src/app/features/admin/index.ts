/** Public API of the admin feature: what the app routes may load. */
export const loadAdminRoutes = () => import('./admin.routes').then((m) => m.adminRoutes);
