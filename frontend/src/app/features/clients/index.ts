/** Public API of the clients feature: what the app routes may load. */
export const loadClientsRoutes = () => import('./clients.routes').then((m) => m.clientsRoutes);
