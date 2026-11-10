import { createActionGroup, props } from '@ngrx/store';
import { TaskDto, TaskPositionDto } from '../api/types';

/**
 * What the project hub told this tab (ADR 0023). The service drops the events this tab caused itself
 * (`originClientId`), so every action here is somebody else's change.
 */
export const RealtimeActions = createActionGroup({
  source: 'Realtime',
  events: {
    'Task Changed': props<{ projectId: string; task: TaskDto }>(),
    'Task Deleted': props<{ projectId: string; taskId: string }>(),
    'Tasks Reordered': props<{ projectId: string; tasks: TaskPositionDto[] }>(),
    'Comments Changed': props<{ projectId: string; taskId: string }>(),
    /** Events may have been missed (reconnected, tab visible again, or a change held back during a move): reload. */
    'Resync Requested': props<{ projectId: string }>(),
  },
});
