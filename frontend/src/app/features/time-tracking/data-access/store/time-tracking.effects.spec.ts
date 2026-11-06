import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { TimeTrackingApiService } from '../time-tracking-api.service';
import { anEntry } from './testing';
import { TimeEntriesApiActions, TimeEntriesPageActions, TimeReportApiActions, TimeReportPageActions } from './time-tracking.actions';
import { createEntry, deleteEntry, exportCsv, loadSummary, loadTaskTime, updateEntry } from './time-tracking.effects';
import { initialTimeTrackingState, timeTrackingFeature } from './time-tracking.feature';

function storeWith(state: typeof initialTimeTrackingState): Store {
  return { select: (selector: unknown) => of(selector === timeTrackingFeature.selectTimeTrackingState ? state : undefined) } as unknown as Store;
}

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const open = { ...initialTimeTrackingState, projectId: 'p1', taskId: 't1' };
const input = { date: '2026-11-03', minutes: 90, note: null };
const filters = { from: '2026-11-01', to: null, userId: null };

describe('time tracking effects', () => {
  it('loads the hours of the opened task', () => {
    const time = { entries: [anEntry({ id: 'a' })], totalMinutes: 60, estimatedMinutes: null };
    const api = { list: vi.fn(() => of(time)) } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeEntriesPageActions.opened({ projectId: 'p1', taskId: 't1' })) as Actions;

    expect(run(loadTaskTime(actions$, api))).toEqual([TimeEntriesApiActions.taskTimeLoaded({ taskId: 't1', time })]);
    expect(api.list).toHaveBeenCalledWith('p1', 't1');
  });

  it('creates an entry and hands the outcome to its form', () => {
    const created = anEntry({ id: 'a', minutes: 90 });
    const api = { create: vi.fn(() => of(created)) } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeEntriesPageActions.createSubmitted({ entry: input, submissionId: 3 })) as Actions;

    const emitted = run(createEntry(actions$, storeWith(open), api));

    expect(api.create).toHaveBeenCalledWith('p1', 't1', input);
    expect(emitted).toEqual([TimeEntriesApiActions.entrySaved({ entry: created, message: 'time.logged', submissionId: 3 })]);
  });

  it('reports a rejected entry with the API error and the submission id', () => {
    const api = {
      create: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'TimeEntries.FutureDate' } }))),
    } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeEntriesPageActions.createSubmitted({ entry: input, submissionId: 5 })) as Actions;

    expect(run(createEntry(actions$, storeWith(open), api))).toEqual([
      TimeEntriesApiActions.entrySaveFailed({ error: { code: 'TimeEntries.FutureDate', fields: {} }, submissionId: 5 }),
    ]);
  });

  it('does not call the API without an open task', () => {
    const api = { create: vi.fn() } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeEntriesPageActions.createSubmitted({ entry: input, submissionId: 1 })) as Actions;

    const emitted = run(createEntry(actions$, storeWith(initialTimeTrackingState), api));

    expect(api.create).not.toHaveBeenCalled();
    expect(emitted).toEqual([TimeEntriesApiActions.entrySaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 1 })]);
  });

  it('updates and deletes an entry of the open task', () => {
    const saved = anEntry({ id: 'a', minutes: 15 });
    const api = {
      update: vi.fn(() => of(saved)),
      delete: vi.fn(() => of(undefined)),
    } as unknown as TimeTrackingApiService;

    const updated = run(
      updateEntry(of(TimeEntriesPageActions.updateSubmitted({ entryId: 'a', changes: input, submissionId: 2 })) as Actions, storeWith(open), api),
    );
    const deleted = run(deleteEntry(of(TimeEntriesPageActions.deleteConfirmed({ entryId: 'a' })) as Actions, storeWith(open), api));

    expect(api.update).toHaveBeenCalledWith('p1', 't1', 'a', input);
    expect(updated).toEqual([TimeEntriesApiActions.entrySaved({ entry: saved, message: 'time.saved', submissionId: 2 })]);
    expect(api.delete).toHaveBeenCalledWith('p1', 't1', 'a');
    expect(deleted).toEqual([TimeEntriesApiActions.entryDeleted({ entryId: 'a' })]);
  });

  it('loads the report with the filters it was opened with', () => {
    const summary = { totalMinutes: 0, byUser: [], byTask: [] };
    const api = { summary: vi.fn(() => of(summary)) } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeReportPageActions.opened({ projectId: 'p1', filters })) as Actions;

    expect(run(loadSummary(actions$, api))).toEqual([TimeReportApiActions.summaryLoaded({ projectId: 'p1', summary })]);
    expect(api.summary).toHaveBeenCalledWith('p1', filters);
  });

  it('saves the exported CSV as a file', () => {
    const blob = new Blob(['a,b']);
    const api = { summaryCsv: vi.fn(() => of(blob)) } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeReportPageActions.exportClicked({ projectId: 'p1', filters })) as Actions;

    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:csv');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const downloads: string[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push(`${this.download} <- ${this.href}`);
    });

    expect(run(exportCsv(actions$, api))).toEqual([TimeReportApiActions.exportDone()]);

    expect(api.summaryCsv).toHaveBeenCalledWith('p1', filters);
    expect(create).toHaveBeenCalledWith(blob);
    expect(downloads).toEqual(['time-summary.csv <- blob:csv']);
    expect(revoke).toHaveBeenCalledWith('blob:csv');
    click.mockRestore();
    create.mockRestore();
    revoke.mockRestore();
  });

  it('reports a failed export', async () => {
    const api = {
      summaryCsv: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 403, error: {} }))),
    } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeReportPageActions.exportClicked({ projectId: 'p1', filters })) as Actions;

    const emitted: Action[] = [];
    await new Promise<void>((resolve) => {
      exportCsv(actions$, api).subscribe({ next: (action) => emitted.push(action), complete: resolve });
    });

    expect(emitted[0].type).toBe(TimeReportApiActions.exportFailed.type);
  });

  it('keeps the error code of a failed export whose body is a blob', async () => {
    const body = new Blob([JSON.stringify({ code: 'TimeEntries.InvalidRange' })], { type: 'application/problem+json' });
    const api = {
      summaryCsv: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 400, error: body }))),
    } as unknown as TimeTrackingApiService;
    const actions$ = of(TimeReportPageActions.exportClicked({ projectId: 'p1', filters })) as Actions;

    const emitted: Action[] = [];
    await new Promise<void>((resolve) => {
      exportCsv(actions$, api).subscribe({ next: (action) => emitted.push(action), complete: resolve });
    });

    expect(emitted).toEqual([TimeReportApiActions.exportFailed({ error: { code: 'TimeEntries.InvalidRange', fields: {} } })]);
  });
});
