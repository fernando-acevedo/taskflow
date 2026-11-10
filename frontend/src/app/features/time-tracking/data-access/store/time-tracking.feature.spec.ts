import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { parseTimeFilters, toTimeQueryParams } from './time-filters';
import { TimeEntriesApiActions, TimeEntriesPageActions, TimeReportApiActions, TimeReportPageActions } from './time-tracking.actions';
import { initialTimeTrackingState, timeTrackingFeature } from './time-tracking.feature';
import { anEntry } from './testing';

const reduce = timeTrackingFeature.reducer;
const noFilters = { from: null, to: null, userId: null };

describe('time tracking reducer', () => {
  const opened = reduce(initialTimeTrackingState, TimeEntriesPageActions.opened({ projectId: 'p1', taskId: 't1' }));
  const time = (entries = [anEntry({ id: 'a' })]) => ({ entries, totalMinutes: 0, estimatedMinutes: 240 });

  it('keeps the entries newest date first and remembers the estimate', () => {
    const old = anEntry({ id: 'old', date: '2026-11-01' });
    const recent = anEntry({ id: 'recent', date: '2026-11-04' });

    const state = reduce(opened, TimeEntriesApiActions.taskTimeLoaded({ taskId: 't1', time: time([old, recent]) }));

    expect(state.ids).toEqual(['recent', 'old']);
    expect(state.estimatedMinutes).toBe(240);
    expect(state.loading).toBe(false);
  });

  it("drops another task's hours when a different task opens", () => {
    const loaded = reduce(opened, TimeEntriesApiActions.taskTimeLoaded({ taskId: 't1', time: time() }));

    const state = reduce(loaded, TimeEntriesPageActions.opened({ projectId: 'p1', taskId: 't2' }));

    expect(state.ids).toEqual([]);
    expect(state.estimatedMinutes).toBeNull();
  });

  it("ignores a load that answers for a task that's no longer open", () => {
    const state = reduce(opened, TimeEntriesApiActions.taskTimeLoaded({ taskId: 'other', time: time() }));

    expect(state.ids).toEqual([]);
  });

  it('adds, replaces and removes entries, and sums the total', () => {
    let state = reduce(opened, TimeEntriesApiActions.entrySaved({ entry: anEntry({ id: 'a', minutes: 30 }), message: 'time.logged' }));
    state = reduce(state, TimeEntriesApiActions.entrySaved({ entry: anEntry({ id: 'b', minutes: 90 }), message: 'time.logged' }));
    state = reduce(state, TimeEntriesApiActions.entrySaved({ entry: anEntry({ id: 'a', minutes: 45 }), message: 'time.saved' }));

    expect(timeTrackingFeature.selectTotalMinutes.projector(Object.values(state.entities).filter((e) => e !== undefined))).toBe(135);

    state = reduce(state, TimeEntriesApiActions.entryDeleted({ entryId: 'b' }));
    expect(state.ids).toEqual(['a']);
  });

  it("ignores a saved entry of a task that isn't open", () => {
    const state = reduce(opened, TimeEntriesApiActions.entrySaved({ entry: anEntry({ id: 'x', taskId: 'other' }), message: 'm' }));

    expect(state.ids).toEqual([]);
  });

  it("clears another project's report while the new one loads, and ignores a late answer", () => {
    const summary = { totalMinutes: 60, byUser: [], byTask: [] };
    let state = reduce(initialTimeTrackingState, TimeReportPageActions.opened({ projectId: 'p1', filters: noFilters }));
    state = reduce(state, TimeReportApiActions.summaryLoaded({ projectId: 'p1', summary }));
    expect(state.report).toBe(summary);

    state = reduce(state, TimeReportPageActions.opened({ projectId: 'p2', filters: noFilters }));
    expect(state.report).toBeNull();
    state = reduce(state, TimeReportApiActions.summaryLoaded({ projectId: 'p1', summary }));
    expect(state.report).toBeNull();
  });

  it('tracks the export until it ends, either way', () => {
    let state = reduce(initialTimeTrackingState, TimeReportPageActions.exportClicked({ projectId: 'p1', filters: noFilters }));
    expect(state.exporting).toBe(true);
    state = reduce(state, TimeReportApiActions.exportDone());
    expect(state.exporting).toBe(false);
    state = reduce(state, TimeReportPageActions.exportClicked({ projectId: 'p1', filters: noFilters }));
    state = reduce(state, TimeReportApiActions.exportFailed({ error: { code: 'unexpected', fields: {} } }));
    expect(state.exporting).toBe(false);
  });

  it.each([
    ['logout', ShellActions.logoutClicked()],
    ['session expiry', AuthApiActions.sessionExpired()],
  ])('forgets everything on %s', (_name, action) => {
    const loaded = reduce(opened, TimeEntriesApiActions.entrySaved({ entry: anEntry({ id: 'a' }), message: 'm' }));

    expect(reduce(loaded, action)).toEqual(initialTimeTrackingState);
  });
});

describe('time filters', () => {
  it('reads valid dates and a user from the query string and drops the rest', () => {
    expect(parseTimeFilters({ from: '2026-11-01', to: 'tomorrow', userId: 'u1' })).toEqual({
      from: '2026-11-01',
      to: null,
      userId: 'u1',
    });
  });

  it('writes null for the filters that are not set, so a merge removes them', () => {
    expect(toTimeQueryParams({ from: '2026-11-01', to: null, userId: null })).toEqual({
      from: '2026-11-01',
      to: null,
      userId: null,
    });
  });
});
