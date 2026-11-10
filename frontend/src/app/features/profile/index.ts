/** Public API of the profile feature: what the app routes may load. */
export const loadProfileRoutes = () => import('./profile.routes').then((m) => m.profileRoutes);
