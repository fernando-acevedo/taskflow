import { TestBed } from '@angular/core/testing';
import { Actions } from '@ngrx/effects';
import { Action } from '@ngrx/store';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { Subject, of, throwError } from 'rxjs';
import { RealtimeActions } from '@core/realtime/realtime.actions';
import { Project, ProjectMember, ProjectsApiService } from '../projects-api.service';
import { ProjectsApiActions } from './projects.actions';
import { loadProject } from './projects.effects';
import { projectsFeature } from './projects.feature';

describe('loadProject', () => {
  function run(api: Pick<ProjectsApiService, 'get' | 'members'>, action: Action): Action[] {
    TestBed.configureTestingModule({
      providers: [provideMockStore({ selectors: [{ selector: projectsFeature.selectCurrent, value: null }] })],
    });
    const actions$ = new Subject<Action>();
    const emitted: Action[] = [];
    TestBed.runInInjectionContext(() =>
      loadProject(actions$ as unknown as Actions, TestBed.inject(MockStore), api as ProjectsApiService).subscribe((a) =>
        emitted.push(a),
      ),
    );
    actions$.next(action);
    return emitted;
  }

  it('reloads the project and its members on a realtime resync', () => {
    const project = { id: 'p1' } as Project;
    const get = vi.fn(() => of(project));
    const members = vi.fn(() => of([] as ProjectMember[]));

    const emitted = run({ get, members }, RealtimeActions.resyncRequested({ projectId: 'p1' }));

    expect(get).toHaveBeenCalledWith('p1');
    expect(members).toHaveBeenCalledWith('p1');
    expect(emitted).toEqual([ProjectsApiActions.projectLoaded({ project, members: [] })]);
  });

  it('keeps what the page shows when a resync fails', () => {
    const emitted = run(
      { get: () => throwError(() => new Error('down')), members: () => of([]) },
      RealtimeActions.resyncRequested({ projectId: 'p1' }),
    );

    expect(emitted).toEqual([]);
  });
});
