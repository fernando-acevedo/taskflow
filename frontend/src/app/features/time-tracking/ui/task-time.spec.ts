import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MockStore, provideMockStore } from '@ngrx/store/testing';
import { of } from 'rxjs';
import { authFeature } from '@core/auth/store/auth.feature';
import { LanguageService } from '@core/i18n/language.service';
import { Submissions } from '@shared/data-access/submission';
import { localToday } from '@shared/ui/local-today';
import { projectsFeature } from '@features/projects';
import { anEntry } from '../data-access/store/testing';
import { TimeEntriesPageActions } from '../data-access/store/time-tracking.actions';
import { timeTrackingFeature } from '../data-access/store/time-tracking.feature';
import { TaskTime } from './task-time';

interface Setup {
  me?: string;
  owner?: boolean;
  admin?: boolean;
  canWork?: boolean;
  archived?: boolean;
  estimated?: number | null;
}

describe('TaskTime', () => {
  const dialog = { open: vi.fn() };
  const dispatchAndWait = vi.fn(() => of(null));
  let store: MockStore;

  const entries = [
    anEntry({ id: 'e1', userId: 'u1', userName: 'Ana', minutes: 90, note: 'Wireframes' }),
    anEntry({ id: 'e2', userId: 'u2', userName: 'Bruno', minutes: 30, date: '2026-11-02' }),
  ];

  function render(setup: Setup = {}): HTMLElement {
    const { me = 'u1', owner = false, admin = false, canWork = true, archived = false, estimated = 240 } = setup;
    dialog.open.mockReset();
    dispatchAndWait.mockClear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        TaskTime,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
      providers: [
        provideMockStore({
          selectors: [
            { selector: timeTrackingFeature.selectEntries, value: entries },
            { selector: timeTrackingFeature.selectTotalMinutes, value: 120 },
            { selector: timeTrackingFeature.selectEstimatedMinutes, value: estimated },
            { selector: timeTrackingFeature.selectLoading, value: false },
            { selector: timeTrackingFeature.selectLoadError, value: null },
            { selector: projectsFeature.selectCurrent, value: { isArchived: archived } },
            { selector: projectsFeature.selectCanEditTasks, value: canWork && !archived },
            { selector: projectsFeature.selectIsOwner, value: owner },
            { selector: authFeature.selectIsAdmin, value: admin },
            { selector: authFeature.selectUser, value: { id: me } },
          ],
        }),
        { provide: MatDialog, useValue: dialog },
        { provide: Submissions, useValue: { dispatchAndWait } },
        { provide: LanguageService, useValue: { current: signal('en') } },
      ],
    });
    store = TestBed.inject<MockStore>(MockStore);
    vi.spyOn(store, 'dispatch');
    const fixture = TestBed.createComponent(TaskTime);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.componentRef.setInput('taskId', 't1');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const buttons = (element: HTMLElement): string[][] =>
    Array.from(element.querySelectorAll('.entry')).map((item) =>
      Array.from(item.querySelectorAll('.meta button')).map((button) => button.getAttribute('aria-label') ?? ''),
    );

  it('loads the hours of its task', () => {
    render();

    expect(store.dispatch).toHaveBeenCalledWith(TimeEntriesPageActions.opened({ projectId: 'p1', taskId: 't1' }));
  });

  it('shows the total against the estimate and every entry with user, duration and note', () => {
    const element = render();

    // 120 of 240 minutes logged.
    expect(element.querySelector<HTMLElement>('app-hours-bar .fill')?.style.width).toBe('50%');
    const items = element.querySelectorAll('.entry');
    expect(items).toHaveLength(2);
    expect(items[0].querySelector('.user')?.textContent).toBe('Ana');
    expect(items[0].querySelector('.minutes')?.textContent).toBe('1 h 30 min');
    expect(items[0].querySelector('.entry-note')?.textContent).toBe('Wireframes');
    expect(items[1].querySelector('.entry-note')).toBeNull();
  });

  it('lets the author edit and delete only their own entries', () => {
    expect(buttons(render({ me: 'u1' }))).toEqual([['time.edit', 'time.delete'], []]);
  });

  it('lets the project owner change every entry', () => {
    expect(buttons(render({ me: 'u3', owner: true }))).toEqual([
      ['time.edit', 'time.delete'],
      ['time.edit', 'time.delete'],
    ]);
  });

  it('does not let an admin who is not the owner change other people’s entries', () => {
    expect(buttons(render({ me: 'u3', admin: true }))).toEqual([[], []]);
  });

  it('is read-only in an archived project, even for the owner and the author', () => {
    const element = render({ me: 'u1', owner: true, archived: true });

    expect(buttons(element)).toEqual([[], []]);
    expect(element.querySelector('app-time-entry-form')).toBeNull();
  });

  it('is read-only for someone who is not a member', () => {
    const element = render({ me: 'u1', canWork: false });

    expect(buttons(element)).toEqual([[], []]);
    expect(element.querySelector('app-time-entry-form')).toBeNull();
  });

  it('deletes only after the confirmation', () => {
    const element = render({ me: 'u1' });
    const del = (): void => element.querySelector<HTMLButtonElement>('.entry button[aria-label="time.delete"]')!.click();

    dialog.open.mockReturnValueOnce({ afterClosed: () => of(false) });
    del();
    expect(store.dispatch).not.toHaveBeenCalledWith(TimeEntriesPageActions.deleteConfirmed({ entryId: 'e1' }));

    dialog.open.mockReturnValueOnce({ afterClosed: () => of(true) });
    del();
    expect(store.dispatch).toHaveBeenCalledWith(TimeEntriesPageActions.deleteConfirmed({ entryId: 'e1' }));
  });

  describe('the log form', () => {
    const field = (element: HTMLElement, name: string): HTMLInputElement =>
      element.querySelector<HTMLInputElement>(`app-time-entry-form [formControlName="${name}"]`)!;
    const submit = (element: HTMLElement): void => {
      element.querySelector<HTMLFormElement>('app-time-entry-form form')!.dispatchEvent(new Event('submit'));
    };
    const type = (input: HTMLInputElement, value: string): void => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };

    it('defaults the date to today and does not allow a future one', () => {
      const element = render();

      expect(field(element, 'date').value).toBe(localToday());
      expect(field(element, 'date').max).toBe(localToday());

      type(field(element, 'date'), '2999-01-01');
      type(field(element, 'minutes'), '1:00');
      submit(element);

      expect(dispatchAndWait).not.toHaveBeenCalled();
    });

    it('logs the duration in minutes with a trimmed note', () => {
      const element = render();

      type(field(element, 'minutes'), '1h 15m');
      type(field(element, 'note'), '  Review  ');
      submit(element);

      expect(dispatchAndWait).toHaveBeenCalledWith(
        expect.objectContaining({
          type: TimeEntriesPageActions.createSubmitted.type,
          entry: { date: localToday(), minutes: 75, note: 'Review' },
        }),
      );
    });

    it.each(['', 'abc', '0', '25'])('does not log a duration of "%s"', (text) => {
      const element = render();

      type(field(element, 'minutes'), text);
      submit(element);

      expect(dispatchAndWait).not.toHaveBeenCalled();
    });
  });
});
