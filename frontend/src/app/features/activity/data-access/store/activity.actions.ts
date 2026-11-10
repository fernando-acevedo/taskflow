import { createActionGroup, props } from '@ngrx/store';
import { ApiError } from '@shared/data-access/api-error';
import { ActivityPage } from '../activity-api.service';

/** The project feed and a task's history are kept apart, so one never replaces the other. */
export type ActivityScope = 'project' | 'task';

export const ActivityPageActions = createActionGroup({
  source: 'Activity Page',
  events: {
    /** A feed was shown: the project's (taskId null) or a task's history. */
    Opened: props<{ scope: ActivityScope; projectId: string; taskId: string | null }>(),
    'Load More Clicked': props<{ scope: ActivityScope }>(),
  },
});

export const ActivityApiActions = createActionGroup({
  source: 'Activity API',
  events: {
    /** `append` is false for a first page (it replaces what is shown) and true for "load more". */
    'Page Loaded': props<{ scope: ActivityScope; projectId: string; taskId: string | null; append: boolean; result: ActivityPage }>(),
    'Load Failed': props<{ scope: ActivityScope; projectId: string; taskId: string | null; error: ApiError }>(),
  },
});
