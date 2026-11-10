/** Public API of the status feature: what the app routes may load. */
export const loadStatusRoutes = () => import('./status.routes').then((m) => m.statusRoutes);
