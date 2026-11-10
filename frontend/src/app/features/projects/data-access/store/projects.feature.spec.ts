import { Project } from '../projects-api.service';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { ProjectPageActions, ProjectsApiActions, ProjectsPageActions } from './projects.actions';
import { initialProjectsState, projectsFeature } from './projects.feature';

const project: Project = {
  id: 'p1',
  name: 'Website',
  description: null,
  clientId: 'c1',
  clientName: 'Acme',
  isArchived: false,
  myRole: 'Member',
  memberCount: 2,
  createdAt: '2026-10-19T12:00:00Z',
  updatedAt: '2026-10-19T12:00:00Z',
  rowVersion: 'AAAAAAAAB9E=',
};

describe('projectsFeature', () => {
  const { reducer, selectCanEditTasks, selectIsOwner } = projectsFeature;

  it('keeps the open project while it reloads but clears it when another one opens', () => {
    const loaded = reducer(initialProjectsState, ProjectsApiActions.projectLoaded({ project, members: [] }));

    expect(reducer(loaded, ProjectPageActions.opened({ projectId: 'p1' })).current).toBe(project);
    expect(reducer(loaded, ProjectPageActions.opened({ projectId: 'p2' })).current).toBeNull();
  });

  it('starts again from the first page on a new filter, and loads', () => {
    const onPageThree = { ...initialProjectsState, query: { ...initialProjectsState.query, page: 3 } };

    const state = reducer(onPageThree, ProjectsPageActions.queryChanged({ query: { search: 'web' } }));

    expect(state.query).toEqual({ ...initialProjectsState.query, search: 'web', page: 1 });
    expect(state.loading).toBe(true);
  });

  it('forgets everything on a tenant switch', () => {
    const loaded = reducer(initialProjectsState, ProjectsApiActions.projectLoaded({ project, members: [] }));

    expect(reducer(loaded, TenantActions.tenantSwitched({ tenantId: 't' }))).toEqual(initialProjectsState);
  });

  it('lets members edit tasks, but not admins outside the project or anyone in an archived project', () => {
    expect(selectCanEditTasks.projector(project)).toBe(true);
    expect(selectCanEditTasks.projector({ ...project, myRole: null })).toBe(false);
    expect(selectCanEditTasks.projector({ ...project, isArchived: true })).toBe(false);
    expect(selectIsOwner.projector(project)).toBe(false);
    expect(selectIsOwner.projector({ ...project, myRole: 'Owner' })).toBe(true);
  });

  it('stops saving when a change fails', () => {
    const saving = reducer(initialProjectsState, ProjectPageActions.archiveConfirmed());

    const state = reducer(saving, ProjectsApiActions.projectChangeFailed({ error: { code: 'Projects.Forbidden', fields: {} } }));

    expect(saving.saving).toBe(true);
    expect(state.saving).toBe(false);
  });
});
