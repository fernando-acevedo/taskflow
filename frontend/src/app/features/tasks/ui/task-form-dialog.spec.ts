import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
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

  it('shows the external key and the titles of the dependencies, read-only', async () => {
    const fixture = render({
      task: aTask({ id: 'a', externalKey: 'EXT-1', dependsOn: ['b'] }),
      tasks: [aTask({ id: 'b', title: 'Design' })],
    });
    const text = (fixture.nativeElement as HTMLElement).querySelector('.planning')?.textContent ?? '';

    expect(text).toContain('EXT-1');
    expect(text).toContain('Design');
    await expectNoAxeViolations(fixture);
  });

  function field(fixture: ReturnType<typeof render>, name: string): HTMLInputElement | HTMLTextAreaElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement | HTMLTextAreaElement>(`[formControlName="${name}"]`)!;
  }

  function type(fixture: ReturnType<typeof render>, name: string, value: string): void {
    const element = field(fixture, name);
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function submit(fixture: ReturnType<typeof render>): void {
    (fixture.nativeElement as HTMLElement).querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
  }

  it('edits the start date, the blocked flag and the status note, and sends them', () => {
    const save = vi.fn(() => of(null));
    const fixture = render({
      task: aTask({ id: 'a', startDate: '2026-11-02', dueDate: '2026-11-20', isBlocked: false, statusNote: null }),
      save,
    });
    expect(field(fixture, 'startDate').value).toBe('2026-11-02');

    type(fixture, 'startDate', '2026-11-05');
    type(fixture, 'statusNote', '  waiting for the client ');
    (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('mat-checkbox input')?.click();
    submit(fixture);

    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({ startDate: '2026-11-05', dueDate: '2026-11-20', isBlocked: true, statusNote: 'waiting for the client' }),
    );
  });

  it('sends empty values as null', () => {
    const save = vi.fn(() => of(null));
    const fixture = render({ task: aTask({ id: 'a', startDate: '2026-11-02', isBlocked: true, statusNote: 'x', dueDate: null }), save });

    type(fixture, 'startDate', '');
    type(fixture, 'statusNote', '');
    submit(fixture);

    expect(save).toHaveBeenCalledWith(expect.objectContaining({ startDate: null, statusNote: null, isBlocked: true }));
  });

  it('does not save a start date after the due date', () => {
    const save = vi.fn(() => of(null));
    const fixture = render({ task: aTask({ id: 'a', dueDate: '2026-11-10' }), save });

    type(fixture, 'startDate', '2026-11-12');
    submit(fixture);

    expect(save).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-error')?.textContent).toContain('startAfterDue');
  });

  it('shows the error of the API under the start date', () => {
    const save = vi.fn(() => of({ code: 'Tasks.StartAfterDue', fields: {} }));
    const fixture = render({ task: aTask({ id: 'a', dueDate: '2026-11-10' }), save });

    submit(fixture);

    expect(save).toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-error')?.textContent).toContain('startAfterDue');
  });

  it('shows validation messages of the API under the status note', () => {
    const save = vi.fn(() => of({ code: 'Validation', fields: { statusNote: ['Too long.'] } }));
    const fixture = render({ task: aTask({ id: 'a' }), save });

    submit(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('mat-error')?.textContent).toContain('Too long.');
  });

  it('has no accessibility violations when editing, with comments, time and history', async () => {
    await expectNoAxeViolations(render({ task: aTask({ id: 'a', title: 'Write brief' }) }));
  });
});
