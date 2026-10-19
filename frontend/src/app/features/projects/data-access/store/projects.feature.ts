import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { ApiError } from '../../../../core/http/api-error';
import { ClientOption, Project, ProjectMember, ProjectQuery } from '../projects-api.service';
import { ProjectPageActions, ProjectsApiActions, ProjectsPageActions } from './projects.actions';

export interface ProjectsState extends EntityState<Project> {
  query: ProjectQuery;
  totalCount: number;
  loading: boolean;
  loadError: ApiError | null;
  clientOptions: ClientOption[];
  /** The open project and its members. */
  current: Project | null;
  members: ProjectMember[];
  currentLoading: boolean;
  currentError: ApiError | null;
  saving: boolean;
}

const adapter = createEntityAdapter<Project>();

export const initialProjectsState: ProjectsState = adapter.getInitialState({
  query: { search: '', clientId: null, archived: false, page: 1, pageSize: 20 },
  totalCount: 0,
  loading: false,
  loadError: null,
  clientOptions: [],
  current: null,
  members: [],
  currentLoading: false,
  currentError: null,
  saving: false,
});

const savingActions = [
  ProjectsPageActions.createSubmitted,
  ProjectPageActions.updateSubmitted,
  ProjectPageActions.archiveConfirmed,
  ProjectPageActions.restoreClicked,
  ProjectPageActions.memberAddSubmitted,
  ProjectPageActions.memberRoleChanged,
  ProjectPageActions.memberRemovalConfirmed,
  ProjectPageActions.leaveConfirmed,
] as const;

export const projectsFeature = createFeature({
  name: 'projects',
  reducer: createReducer(
    initialProjectsState,
    // Another user may sign in next in this tab; nothing of the previous session may show.
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): ProjectsState => initialProjectsState),
    on(ProjectsPageActions.opened, (state): ProjectsState => ({ ...state, loading: true, loadError: null })),
    on(
      ProjectsPageActions.queryChanged,
      (state, { query }): ProjectsState => ({
        ...state,
        query: { ...state.query, page: 1, ...query },
        loading: true,
        loadError: null,
      }),
    ),
    on(
      ProjectsApiActions.projectsLoaded,
      (state, { page }): ProjectsState =>
        adapter.setAll(page.items, { ...state, totalCount: page.totalCount, loading: false }),
    ),
    on(ProjectsApiActions.projectsLoadFailed, (state, { error }): ProjectsState => ({ ...state, loading: false, loadError: error })),
    on(ProjectsApiActions.clientOptionsLoaded, (state, { clients }): ProjectsState => ({ ...state, clientOptions: clients })),
    on(
      ProjectPageActions.opened,
      (state, { projectId }): ProjectsState => ({
        ...state,
        // Keep showing the project while it reloads; clear it when switching to another one.
        current: state.current?.id === projectId ? state.current : null,
        members: state.current?.id === projectId ? state.members : [],
        currentLoading: true,
        currentError: null,
      }),
    ),
    on(
      ProjectsApiActions.projectLoaded,
      (state, { project, members }): ProjectsState => ({
        ...state,
        current: project,
        members,
        currentLoading: false,
        saving: false,
      }),
    ),
    on(
      ProjectsApiActions.projectLoadFailed,
      (state, { error }): ProjectsState => ({ ...state, currentLoading: false, saving: false, currentError: error }),
    ),
    on(...savingActions, (state): ProjectsState => ({ ...state, saving: true })),
    on(
      ProjectsApiActions.projectCreated,
      ProjectsApiActions.projectChangeFailed,
      ProjectsApiActions.projectLeft,
      (state): ProjectsState => ({ ...state, saving: false }),
    ),
  ),
  extraSelectors: ({ selectProjectsState, selectCurrent }) => ({
    ...adapter.getSelectors(selectProjectsState),
    selectIsOwner: createSelector(selectCurrent, (project) => project?.myRole === 'Owner'),
    /** Members can change tasks; admins who are not members only read, and archived projects are read-only. */
    selectCanEditTasks: createSelector(selectCurrent, (project) => !!project && project.myRole !== null && !project.isArchived),
  }),
});
