import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { provideStore } from '@ngrx/store';
import { authFeature } from '../../../core/auth/store/auth.feature';
import { LanguageService } from '../../../core/i18n/language.service';
import { expectNoAxeViolations } from '../../../shared/testing/axe';
import { activityFeature } from '../../activity/data-access/store/activity.feature';
import { commentsFeature } from '../../comments/data-access/store/comments.feature';
import { projectsFeature } from '../../projects/data-access/store/projects.feature';
import { timeTrackingFeature } from '../../time-tracking/data-access/store/time-tracking.feature';
import { aTask } from '../data-access/store/testing';
import { TaskFormData, TaskFormDialog } from './task-form-dialog';

describe('TaskFormDialog', () => {
  function render(data: Partial<TaskFormData>) {
    TestBed.configureTestingModule({
      imports: [
        TaskFormDialog,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        // A real store with the reducers the dialog's tabs read; no effects run, so nothing calls the API.
        provideStore({
          [authFeature.name]: authFeature.reducer,
          [projectsFeature.name]: projectsFeature.reducer,
          [commentsFeature.name]: commentsFeature.reducer,
          [timeTrackingFeature.name]: timeTrackingFeature.reducer,
          [activityFeature.name]: activityFeature.reducer,
        }),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { task: null, members: [], labels: [], readOnly: false, save: vi.fn(), ...data } },
        { provide: LanguageService, useValue: { current: signal('en') } },
      ],
    });
    const fixture = TestBed.createComponent(TaskFormDialog);
    fixture.detectChanges();
    return fixture;
  }

  it('has no accessibility violations when creating', async () => {
    await expectNoAxeViolations(render({}));
  });

  it('shows the external key, the dates and the titles of the dependencies, read-only', async () => {
    const fixture = render({
      task: aTask({ id: 'a', externalKey: 'EXT-1', startDate: '2026-11-02', isBlocked: true, statusNote: 'Waiting', dependsOn: ['b'] }),
      tasks: [aTask({ id: 'b', title: 'Design' })],
    });
    const text = (fixture.nativeElement as HTMLElement).querySelector('.planning')?.textContent ?? '';

    expect(text).toContain('EXT-1');
    expect(text).toContain('Waiting');
    expect(text).toContain('Design');
    await expectNoAxeViolations(fixture);
  });

  it('has no accessibility violations when editing, with comments, time and history', async () => {
    await expectNoAxeViolations(render({ task: aTask({ id: 'a', title: 'Write brief' }) }));
  });
});
