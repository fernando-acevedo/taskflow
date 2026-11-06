import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { BehaviorSubject, of } from 'rxjs';
import { Notifier } from '../../../core/notifications/notifier';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { groupByStatus } from '../data-access/store/board';
import { TasksPageActions } from '../data-access/store/tasks.actions';
import { tasksFeature } from '../data-access/store/tasks.feature';
import { aTask } from '../data-access/store/testing';
import { expectNoAxeViolations } from '../../../shared/testing/axe';
import { TaskEditor } from '../ui/task-editor';
import { BoardPage } from './board-page';

const tasks = [
  aTask({ id: 'a', title: 'Write brief', status: 'ToDo', position: 0 }),
  aTask({ id: 'b', title: 'Pick fonts', status: 'ToDo', position: 1 }),
  aTask({ id: 'c', title: 'Launch', status: 'Done', position: 0 }),
];

describe('BoardPage', () => {
  let store: MockStore;
  let editor: { open: ReturnType<typeof vi.fn> };
  let router: { navigate: ReturnType<typeof vi.fn> };
  let queryParams$: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let notifier: { error: ReturnType<typeof vi.fn>; errorMessage: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    editor = { open: vi.fn() };
    router = { navigate: vi.fn(() => Promise.resolve(true)) };
    notifier = { error: vi.fn(), errorMessage: vi.fn() };
  });

  function render(canEdit = true, linkedTask: string | null = null, options: { loading?: boolean; projectId?: string } = {}): HTMLElement {
    const route = {
      queryParamMap: (queryParams$ = new BehaviorSubject(convertToParamMap(linkedTask ? { task: linkedTask } : {}))),
      parent: { paramMap: of(convertToParamMap({ projectId: 'p1' })) },
    };
    TestBed.configureTestingModule({
      imports: [
        BoardPage,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: tasksFeature.selectColumns, value: groupByStatus(tasks) },
            { selector: tasksFeature.selectLoading, value: options.loading ?? false },
            { selector: tasksFeature.selectLoadError, value: null },
            { selector: tasksFeature.selectMoving, value: false },
            { selector: tasksFeature.selectProjectId, value: options.projectId ?? 'p1' },
            { selector: projectsFeature.selectCanEditTasks, value: canEdit },
            { selector: projectsFeature.selectMemberNames, value: new Map<string, string>() },
          ],
        }),
        { provide: TaskEditor, useValue: editor },
        { provide: ActivatedRoute, useValue: route },
        { provide: Router, useValue: router },
        { provide: Notifier, useValue: notifier },
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');
    const fixture = TestBed.createComponent(BoardPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  /** Opens the move menu of the card titled `title` and returns the menu's items. */
  function openMoveMenu(element: HTMLElement, title: string): HTMLButtonElement[] {
    const card = [...element.querySelectorAll<HTMLElement>('.card')].find((node) => node.textContent?.includes(title));
    card?.querySelector<HTMLButtonElement>('button.move')?.click();
    TestBed.tick();
    return [...document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-panel button[mat-menu-item]')];
  }

  it('has no accessibility violations, with the move menu open too', async () => {
    const element = render();
    await expectNoAxeViolations(element);

    openMoveMenu(element, 'Write brief');
    await expectNoAxeViolations(document.body);
  });

  it('moves a task to another column from the keyboard-reachable menu', () => {
    const element = render();

    const items = openMoveMenu(element, 'Write brief');
    items.find((item) => item.textContent?.includes('tasks.statuses.Done'))?.click();

    // Done already holds one task, so the moved one goes after it.
    expect(store.dispatch).toHaveBeenCalledWith(TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 1 }));
  });

  it('offers moving down but not up for the first task of a column', () => {
    const element = render();

    const labels = openMoveMenu(element, 'Write brief').map((item) => item.textContent?.trim());

    expect(labels).toContain('arrow_downward tasks.moveDown');
    expect(labels).not.toContain('arrow_upward tasks.moveUp');
    expect(labels).not.toContain('tasks.statuses.ToDo');
  });

  it('has no move buttons when the user can only read the project', () => {
    const element = render(false);

    expect(element.querySelectorAll('button.move')).toHaveLength(0);
  });

  describe('?task= link', () => {
    it('opens the linked task and removes the parameter', () => {
      render(true, 'b');
      TestBed.tick();

      expect(editor.open).toHaveBeenCalledWith(expect.objectContaining({ id: 'b' }));
      expect(router.navigate).toHaveBeenCalledWith(
        [],
        expect.objectContaining({ queryParams: { task: null }, queryParamsHandling: 'merge' }),
      );
    });

    it('says so when the task no longer exists', () => {
      render(true, 'gone');
      TestBed.tick();

      expect(editor.open).not.toHaveBeenCalled();
      expect(notifier.error).toHaveBeenCalledWith({ code: 'Tasks.NotFound', fields: {} });
    });

    it('waits for the tasks of this project to load', () => {
      render(true, 'b', { loading: true });
      TestBed.tick();

      expect(editor.open).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('waits while the store still holds another project', () => {
      render(true, 'b', { projectId: 'other' });
      TestBed.tick();

      expect(editor.open).not.toHaveBeenCalled();
    });

    it('opens the same task again when the link is used a second time', () => {
      render(true, 'b');
      TestBed.tick();
      queryParams$.next(convertToParamMap({}));
      TestBed.tick();
      queryParams$.next(convertToParamMap({ task: 'b' }));
      TestBed.tick();

      expect(editor.open).toHaveBeenCalledTimes(2);
    });

    it('opens nothing without the parameter', () => {
      render();
      TestBed.tick();

      expect(editor.open).not.toHaveBeenCalled();
    });
  });
});
