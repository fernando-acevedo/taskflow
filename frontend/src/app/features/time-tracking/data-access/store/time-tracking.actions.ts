import { createActionGroup, emptyProps, props } from '@ngrx/store';
import { ApiError } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { TaskTime, TimeEntry, TimeEntryInput, TimeFilters, TimeSummary } from '../time-tracking-api.service';

export const TimeEntriesPageActions = createActionGroup({
  source: 'Time Entries Page',
  events: {
    /** The hours of a task were shown. */
    Opened: props<{ projectId: string; taskId: string }>(),
    'Create Submitted': props<{ entry: TimeEntryInput } & Required<Submission>>(),
    'Update Submitted': props<{ entryId: string; changes: TimeEntryInput } & Required<Submission>>(),
    'Delete Confirmed': props<{ entryId: string }>(),
  },
});

export const TimeEntriesApiActions = createActionGroup({
  source: 'Time Entries API',
  events: {
    'Task Time Loaded': props<{ taskId: string; time: TaskTime }>(),
    'Task Time Load Failed': props<{ error: ApiError }>(),
    'Entry Saved': props<{ entry: TimeEntry; message: string } & Submission>(),
    'Entry Deleted': props<{ entryId: string }>(),
    'Entry Save Failed': props<{ error: ApiError } & Submission>(),
  },
});

export const TimeReportPageActions = createActionGroup({
  source: 'Time Report Page',
  events: {
    /** The report was shown, or its filters changed. */
    Opened: props<{ projectId: string; filters: TimeFilters }>(),
    'Export Clicked': props<{ projectId: string; filters: TimeFilters }>(),
  },
});

export const TimeReportApiActions = createActionGroup({
  source: 'Time Report API',
  events: {
    'Summary Loaded': props<{ projectId: string; summary: TimeSummary }>(),
    'Summary Load Failed': props<{ error: ApiError }>(),
    'Export Done': emptyProps(),
    'Export Failed': props<{ error: ApiError }>(),
  },
});
