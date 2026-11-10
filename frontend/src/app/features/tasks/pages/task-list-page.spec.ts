import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of } from 'rxjs';
import { LanguageService } from '@core/i18n/language.service';
import { expectNoAxeViolations } from '@shared/testing/axe';
import { labelsFeature } from '@features/labels';
import { projectsFeature } from '@features/projects';
import { NO_FILTERS, TaskFilters } from '../data-access/store/task-filters';
import { tasksFeature } from '../data-access/store/tasks.feature';
import { TaskEditor } from '../ui/task-editor';
import { TaskListPage } from './task-list-page';

describe('TaskListPage label filter', () => {
  const navigate = vi.fn(() => Promise.resolve(true));
  const route = { parent: { paramMap: of(convertToParamMap({ projectId: 'p1' })) } };

  function render(filters: TaskFilters, labelsState: { projectId: string | null; loading: boolean }): HTMLElement {
    navigate.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TaskListPage,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: tasksFeature.selectFilters, value: filters },
            { selector: tasksFeature.selectFilteredTasks, value: [] },
            { selector: tasksFeature.selectTotal, value: 0 },
            { selector: tasksFeature.selectLoading, value: false },
            { selector: projectsFeature.selectCanEditTasks, value: true },
            { selector: projectsFeature.selectMembers, value: [] },
            { selector: projectsFeature.selectMemberNames, value: new Map<string, string>() },
            { selector: labelsFeature.selectAll, value: [{ id: 'l1', name: 'Bug', color: '#D32F2F' }] },
            { selector: labelsFeature.selectProjectId, value: labelsState.projectId },
            { selector: labelsFeature.selectLoading, value: labelsState.loading },
            { selector: labelsFeature.selectLoadError, value: null },
          ],
        }),
        { provide: Router, useValue: { navigate } },
        { provide: ActivatedRoute, useValue: route },
        { provide: LanguageService, useValue: { current: signal('en') } },
        { provide: TaskEditor, useValue: { open: vi.fn() } },
      ],
    });
    TestBed.inject<MockStore>(MockStore);
    const fixture = TestBed.createComponent(TaskListPage);
    fixture.detectChanges();
    TestBed.tick();
    return fixture.nativeElement as HTMLElement;
  }

  it('has no accessibility violations', async () => {
    await expectNoAxeViolations(render(NO_FILTERS, { projectId: 'p1', loading: false }));
  });

  it('clears a label that no longer belongs to the project', () => {
    render({ ...NO_FILTERS, label: 'gone' }, { projectId: 'p1', loading: false });

    expect(navigate).toHaveBeenCalledWith([], { relativeTo: route, queryParams: expect.objectContaining({ label: null }) as unknown, replaceUrl: true });
  });

  it('keeps an existing label', () => {
    render({ ...NO_FILTERS, label: 'l1' }, { projectId: 'p1', loading: false });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('waits for the labels to load before judging the filter', () => {
    render({ ...NO_FILTERS, label: 'gone' }, { projectId: 'p1', loading: true });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores labels that still belong to the previous project', () => {
    render({ ...NO_FILTERS, label: 'gone' }, { projectId: 'p0', loading: false });

    expect(navigate).not.toHaveBeenCalled();
  });
});
