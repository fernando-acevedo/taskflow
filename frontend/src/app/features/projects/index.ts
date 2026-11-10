/** Public API of the projects feature: what other features may import. */
export { projectsFeature } from './data-access/store/projects.feature';
export type { ProjectMember } from './data-access/projects-api.service';
/** App routes load this barrel on demand. */
export const loadProjectsRoutes = () => import('./projects.routes').then((m) => m.projectsRoutes);
