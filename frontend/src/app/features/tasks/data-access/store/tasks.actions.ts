import { createActionGroup, props } from '@ngrx/store';
import { ApiError } from '@shared/data-access/api-error';
import { Submission } from '@shared/data-access/submission';
import { MoveResult, Task, TaskInput, TaskStatus } from '../tasks-api.service';

/** Used by both views of a project's tasks: the board and the list. */
export const TasksPageActions = createActionGroup({
  source: 'Tasks Page',
  events: {
    Opened: props<{ projectId: string }>(),
    /** A drag and drop, or the board's "Move to" menu (the keyboard way to do the same). */
    'Task Dropped': props<{ taskId: string; status: TaskStatus; position: number }>(),
    'Create Submitted': props<{ task: TaskInput } & Required<Submission>>(),
    'Update Submitted': props<{ taskId: string; changes: TaskInput; rowVersion: string } & Required<Submission>>(),
    'Delete Confirmed': props<{ taskId: string }>(),
    /** The edit dialog asks for the server's version after a stale save. */
    'Refresh Requested': props<{ taskId: string } & Required<Submission>>(),
  },
});

export const TasksApiActions = createActionGroup({
  source: 'Tasks API',
  events: {
    'Tasks Loaded': props<{ projectId: string; tasks: Task[] }>(),
    'Tasks Load Failed': props<{ error: ApiError }>(),
    'Task Saved': props<{ task: Task; message: string } & Submission>(),
    'Task Deleted': props<{ taskId: string }>(),
    'Task Save Failed': props<{ error: ApiError } & Submission>(),
    /** The server's current version of a task, fetched on request. */
    'Task Refreshed': props<{ task: Task } & Submission>(),
    /** The move was stored; `reordered` holds the neighbors' new positions and versions, so no reload is needed. */
    'Task Moved': props<{ result: MoveResult }>(),
    'Task Move Failed': props<{ error: ApiError }>(),
  },
});
