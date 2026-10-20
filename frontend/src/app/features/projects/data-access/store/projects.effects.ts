import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { Observable, catchError, concatMap, filter, forkJoin, map, of, switchMap, tap, withLatestFrom } from 'rxjs';
import { User } from '../../../../core/auth/auth.models';
import { authFeature } from '../../../../core/auth/store/auth.feature';
import { toApiError } from '../../../../core/http/api-error';
import { Notifier } from '../../../../core/notifications/notifier';
import { Project, ProjectsApiService } from '../projects-api.service';
import { ProjectPageActions, ProjectsApiActions, ProjectsPageActions } from './projects.actions';
import { projectsFeature } from './projects.feature';

export const loadProjects = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ProjectsApiService)) =>
    actions$.pipe(
      ofType(ProjectsPageActions.opened, ProjectsPageActions.queryChanged),
      withLatestFrom(store.select(projectsFeature.selectQuery)),
      switchMap(([, query]) =>
        api.list(query).pipe(
          map((page) => ProjectsApiActions.projectsLoaded({ page })),
          catchError((error: unknown) => of(ProjectsApiActions.projectsLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/** Clients for the filter and the project form. A failure only leaves the picker empty. */
export const loadClientOptions = createEffect(
  (actions$ = inject(Actions), api = inject(ProjectsApiService)) =>
    actions$.pipe(
      ofType(ProjectsPageActions.opened, ProjectPageActions.opened),
      switchMap(() =>
        api.clientOptions().pipe(
          map((clients) => ProjectsApiActions.clientOptionsLoaded({ clients })),
          catchError(() => of(ProjectsApiActions.clientOptionsLoaded({ clients: [] }))),
        ),
      ),
    ),
  { functional: true },
);

export const createProject = createEffect(
  (actions$ = inject(Actions), api = inject(ProjectsApiService)) =>
    actions$.pipe(
      ofType(ProjectsPageActions.createSubmitted),
      concatMap(({ project, submissionId }) =>
        api.create(project).pipe(
          map((created) => ProjectsApiActions.projectCreated({ project: created, submissionId })),
          catchError((error: unknown) =>
            of(ProjectsApiActions.projectChangeFailed({ error: toApiError(error), submissionId })),
          ),
        ),
      ),
    ),
  { functional: true },
);

export const openCreatedProject = createEffect(
  (actions$ = inject(Actions), router = inject(Router)) =>
    actions$.pipe(
      ofType(ProjectsApiActions.projectCreated),
      tap(({ project }) => void router.navigate(['/projects', project.id])),
    ),
  { functional: true, dispatch: false },
);

export const loadProject = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ProjectsApiService)) =>
    actions$.pipe(
      ofType(ProjectPageActions.opened, ProjectsApiActions.projectChanged),
      withLatestFrom(store.select(projectsFeature.selectCurrent)),
      map(([action, current]) => (action.type === ProjectPageActions.opened.type ? action.projectId : current?.id)),
      filter((projectId): projectId is string => !!projectId),
      switchMap((projectId) =>
        forkJoin({ project: api.get(projectId), members: api.members(projectId) }).pipe(
          map(({ project, members }) => ProjectsApiActions.projectLoaded({ project, members })),
          catchError((error: unknown) => of(ProjectsApiActions.projectLoadFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

/**
 * Every change to the open project runs here, one at a time and none dropped (two quick role changes both
 * reach the API), and ends with a reload of the project.
 */
export const changeProject = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ProjectsApiService)) =>
    actions$.pipe(
      ofType(
        ProjectPageActions.updateSubmitted,
        ProjectPageActions.archiveConfirmed,
        ProjectPageActions.restoreClicked,
        ProjectPageActions.memberAddSubmitted,
        ProjectPageActions.memberRoleChanged,
        ProjectPageActions.memberRemovalConfirmed,
      ),
      withLatestFrom(store.select(projectsFeature.selectCurrent)),
      filter((pair): pair is [(typeof pair)[0], Project] => pair[1] !== null),
      concatMap(([action, project]) => {
        let request: Observable<unknown>;
        let message: string;
        let submissionId: number | undefined;
        switch (action.type) {
          case ProjectPageActions.updateSubmitted.type:
            request = api.update(project.id, action.project, project.rowVersion);
            message = 'projects.saved';
            submissionId = action.submissionId;
            break;
          case ProjectPageActions.archiveConfirmed.type:
          case ProjectPageActions.restoreClicked.type:
            request = api.setArchived(project.id, action.type === ProjectPageActions.archiveConfirmed.type);
            message = action.type === ProjectPageActions.archiveConfirmed.type ? 'projects.archived' : 'projects.restored';
            break;
          case ProjectPageActions.memberAddSubmitted.type:
            request = api.addMember(project.id, action.email, action.role);
            message = 'members.added';
            submissionId = action.submissionId;
            break;
          case ProjectPageActions.memberRoleChanged.type:
            request = api.changeRole(project.id, action.userId, action.role);
            message = 'members.roleChanged';
            break;
          default:
            request = api.removeMember(project.id, action.userId);
            message = 'members.removed';
        }
        return request.pipe(
          map(() => ProjectsApiActions.projectChanged({ message, submissionId })),
          catchError((error: unknown) =>
            of(ProjectsApiActions.projectChangeFailed({ error: toApiError(error), submissionId })),
          ),
        );
      }),
    ),
  { functional: true },
);

export const leaveProject = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ProjectsApiService)) =>
    actions$.pipe(
      ofType(ProjectPageActions.leaveConfirmed),
      withLatestFrom(store.select(projectsFeature.selectCurrent), store.select(authFeature.selectUser)),
      // Without an open project or a signed-in user there is nobody to remove; the button isn't shown then.
      filter((triple): triple is [(typeof triple)[0], Project, User] => triple[1] !== null && triple[2] !== null),
      concatMap(([, project, user]) =>
        api.removeMember(project.id, user.id).pipe(
          map(() => ProjectsApiActions.projectLeft()),
          catchError((error: unknown) => of(ProjectsApiActions.projectChangeFailed({ error: toApiError(error) }))),
        ),
      ),
    ),
  { functional: true },
);

export const notify = createEffect(
  (actions$ = inject(Actions), notifier = inject(Notifier), router = inject(Router)) =>
    actions$.pipe(
      ofType(ProjectsApiActions.projectChanged, ProjectsApiActions.projectChangeFailed, ProjectsApiActions.projectLeft),
      tap((action) => {
        switch (action.type) {
          case ProjectsApiActions.projectChanged.type:
            notifier.success(action.message);
            break;
          case ProjectsApiActions.projectLeft.type:
            notifier.success('members.left');
            void router.navigate(['/projects']);
            break;
          default:
            notifier.error(action.error);
        }
      }),
    ),
  { functional: true, dispatch: false },
);
