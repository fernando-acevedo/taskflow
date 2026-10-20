import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { getRouterSelectors } from '@ngrx/router-store';
import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '../../../../core/auth/store/auth.actions';
import { authFeature } from '../../../../core/auth/store/auth.feature';
import { ApiError } from '../../../../core/http/api-error';
import { Task } from '../tasks-api.service';
import { compareTasks, groupByStatus, moveTask } from './board';
import { applyTaskFilters, parseTaskFilters } from './task-filters';
import { TasksApiActions, TasksPageActions } from './tasks.actions';

export interface TasksState extends EntityState<Task> {
  projectId: string | null;
  loading: boolean;
  loadError: ApiError | null;
  saving: boolean;
  /**
   * The previous version of the tasks an optimistic move changed, to put back if the API rejects it.
   * Only one move is in flight at a time: the board disables dragging until it settles.
   */
  moveUndo: Task[] | null;
}

const adapter = createEntityAdapter<Task>({ sortComparer: compareTasks });

export const initialTasksState: TasksState = adapter.getInitialState({
  projectId: null,
  loading: false,
  loadError: null,
  saving: false,
  moveUndo: null,
});

const { selectQueryParams } = getRouterSelectors();

export const tasksFeature = createFeature({
  name: 'tasks',
  reducer: createReducer(
    initialTasksState,
    // Another user may sign in next in this tab; nothing of the previous session may show.
    on(ShellActions.logoutClicked, AuthApiActions.sessionExpired, (): TasksState => initialTasksState),
    on(TasksPageActions.opened, (state, { projectId }): TasksState =>
      // Another project's tasks must not flash while this one loads.
      state.projectId === projectId
        ? { ...state, loading: true, loadError: null }
        : adapter.removeAll({ ...state, projectId, loading: true, loadError: null, moveUndo: null }),
    ),
    on(TasksApiActions.tasksLoaded, (state, { projectId, tasks }): TasksState =>
      projectId === state.projectId ? adapter.setAll(tasks, { ...state, loading: false }) : state,
    ),
    on(TasksApiActions.tasksLoadFailed, (state, { error }): TasksState => ({ ...state, loading: false, loadError: error })),
    on(
      TasksPageActions.createSubmitted,
      TasksPageActions.updateSubmitted,
      TasksPageActions.deleteConfirmed,
      (state): TasksState => ({ ...state, saving: true }),
    ),
    on(TasksApiActions.taskSaved, (state, { task }): TasksState => adapter.upsertOne(task, { ...state, saving: false })),
    on(TasksApiActions.taskDeleted, (state, { taskId }): TasksState => adapter.removeOne(taskId, { ...state, saving: false })),
    on(TasksApiActions.taskSaveFailed, (state): TasksState => ({ ...state, saving: false })),
    on(TasksPageActions.taskDropped, (state, { taskId, status, position }): TasksState => {
      const changed = moveTask(Object.values(state.entities) as Task[], taskId, status, position);
      if (changed.length === 0) {
        return state;
      }
      const undo = changed.map((task) => state.entities[task.id]).filter((task): task is Task => task !== undefined);
      return adapter.upsertMany(changed, { ...state, moveUndo: undo });
    }),
    on(TasksApiActions.taskMoved, (state, { task }): TasksState => adapter.upsertOne(task, { ...state, moveUndo: null })),
    on(TasksApiActions.taskMoveFailed, (state): TasksState =>
      adapter.upsertMany(state.moveUndo ?? [], { ...state, moveUndo: null }),
    ),
  ),
  extraSelectors: ({ selectTasksState, selectMoveUndo }) => {
    const { selectAll, selectTotal } = adapter.getSelectors(selectTasksState);
    const selectFilters = createSelector(selectQueryParams, (params) => parseTaskFilters(params ?? {}));
    return {
      selectAll,
      selectTotal,
      selectColumns: createSelector(selectAll, groupByStatus),
      selectMoving: createSelector(selectMoveUndo, (undo) => undo !== null),
      selectFilters,
      selectFilteredTasks: createSelector(selectAll, selectFilters, authFeature.selectUser, (tasks, filters, user) =>
        applyTaskFilters(tasks, filters, user?.id ?? null),
      ),
    };
  },
});
