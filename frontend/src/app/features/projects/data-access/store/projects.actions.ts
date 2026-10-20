import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError, PagedResult } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { ClientOption, Project, ProjectInput, ProjectMember, ProjectQuery, ProjectRole } from '../projects-api.service';

export const ProjectsPageActions = createActionGroup({
  source: 'Projects Page',
  events: {
    Opened: emptyProps(),
    'Query Changed': props<{ query: Partial<ProjectQuery> }>(),
    'Create Submitted': props<{ project: ProjectInput } & Required<Submission>>(),
  },
});

/** The open project: its shell, members tab and settings tab. */
export const ProjectPageActions = createActionGroup({
  source: 'Project Page',
  events: {
    Opened: props<{ projectId: string }>(),
    'Update Submitted': props<{ project: ProjectInput } & Required<Submission>>(),
    'Archive Confirmed': emptyProps(),
    'Restore Clicked': emptyProps(),
    'Member Add Submitted': props<{ email: string; role: ProjectRole } & Required<Submission>>(),
    'Member Role Changed': props<{ userId: string; role: ProjectRole }>(),
    'Member Removal Confirmed': props<{ userId: string }>(),
    'Leave Confirmed': emptyProps(),
  },
});

export const ProjectsApiActions = createActionGroup({
  source: 'Projects API',
  events: {
    'Projects Loaded': props<{ page: PagedResult<Project> }>(),
    'Projects Load Failed': props<{ error: ApiError }>(),
    'Client Options Loaded': props<{ clients: ClientOption[] }>(),
    'Project Created': props<{ project: Project } & Submission>(),
    'Project Loaded': props<{ project: Project; members: ProjectMember[] }>(),
    'Project Load Failed': props<{ error: ApiError }>(),
    /** An edit, archive or membership change succeeded; the project is reloaded. */
    'Project Changed': props<{ message: string } & Submission>(),
    'Project Change Failed': props<{ error: ApiError } & Submission>(),
    'Project Left': emptyProps(),
  },
});
