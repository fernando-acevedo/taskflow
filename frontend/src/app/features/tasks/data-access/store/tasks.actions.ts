import { createActionGroup, props } from '@ngrx/store';
import { ApiError } from '../../../../core/http/api-error';
import { Submission } from '../../../../shared/data-access/submission';
import { Task, TaskInput, TaskStatus } from '../tasks-api.service';

/** Used by both views of a project's tasks: the board and the list. */
export const TasksPageActions = createActionGroup({
  source: 'Tasks Page',
  events: {
    Opened: props<{ projectId: string }>(),
    /** A drag and drop, or the board's "Move to" menu (the keyboard way to do the same). */
    'Task Dropped': props<{ taskId: string; status: TaskStatus; position: number }>(),
    'Create Submitted': props<{ task: TaskInput } & Required<Submission>>(),
    'Update Submitted': props<{ taskId: string; changes: TaskInput } & Required<Submission>>(),
    'Delete Confirmed': props<{ taskId: string }>(),
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
    /** The move was stored; the other tasks of both columns got new positions and versions, so tasks reload. */
    'Task Moved': props<{ task: Task }>(),
    'Task Move Failed': props<{ error: ApiError }>(),
  },
});
