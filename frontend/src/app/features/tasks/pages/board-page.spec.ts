import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { groupByStatus } from '../data-access/store/board';
import { TasksPageActions } from '../data-access/store/tasks.actions';
import { tasksFeature } from '../data-access/store/tasks.feature';
import { aTask } from '../data-access/store/testing';
import { TaskEditor } from '../ui/task-editor';
import { BoardPage } from './board-page';

const tasks = [
  aTask({ id: 'a', title: 'Write brief', status: 'ToDo', position: 0 }),
  aTask({ id: 'b', title: 'Pick fonts', status: 'ToDo', position: 1 }),
  aTask({ id: 'c', title: 'Launch', status: 'Done', position: 0 }),
];

describe('BoardPage', () => {
  let store: MockStore;

  function render(canEdit = true): HTMLElement {
    TestBed.configureTestingModule({
      imports: [
        BoardPage,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: tasksFeature.selectColumns, value: groupByStatus(tasks) },
            { selector: tasksFeature.selectLoading, value: false },
            { selector: tasksFeature.selectLoadError, value: null },
            { selector: tasksFeature.selectMoving, value: false },
            { selector: tasksFeature.selectProjectId, value: 'p1' },
            { selector: projectsFeature.selectCanEditTasks, value: canEdit },
            { selector: projectsFeature.selectMemberNames, value: new Map<string, string>() },
          ],
        }),
        { provide: TaskEditor, useValue: { open: vi.fn() } },
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
});
