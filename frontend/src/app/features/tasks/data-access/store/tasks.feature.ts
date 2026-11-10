import { EntityState, createEntityAdapter } from '@ngrx/entity';
import { getRouterSelectors } from '@ngrx/router-store';
import { createFeature, createReducer, createSelector, on } from '@ngrx/store';
import { AuthApiActions, ShellActions } from '@core/auth/store/auth.actions';
import { RealtimeActions } from '@core/realtime/realtime.actions';
import { TenantActions } from '@core/tenant/store/tenant.actions';
import { authFeature } from '@core/auth/store/auth.feature';
import { ApiError } from '@shared/data-access/api-error';
import { Task } from '../tasks-api.service';
import { compareTasks, groupByStatus, moveTask } from './board';
import { applyTaskFilters, parseTaskFilters } from './task-filters';
import { TasksApiActions, TasksPageActions } from './tasks.actions';

export interface TasksState extends EntityState<Task> {
  projectId: string | null;
  /** Nothing to show yet: the first load of a project. */
  loading: boolean;
  /** The same project loads again: its tasks stay on screen until the response replaces them (stale-while-revalidate). */
  refreshing: boolean;
  loadError: ApiError | null;
  saving: boolean;
  /**
   * The previous version of the tasks an optimistic move changed, to put back if the API rejects it.
   * Only one move is in flight at a time: the board disables dragging until it settles.
   */
  moveUndo: Task[] | null;
  /**
   * Somebody else's change arrived while an own move was in flight and was held back, so as not to overwrite the
   * optimistic state. When the move settles, the tasks are reloaded (see `reloadAfterMove`); a load clears it.
   */
  resyncAfterMove: boolean;
}

/** A task that was deleted must not be put back by reverting a move: drop it from the undo copy. */
function withoutUndoOf(taskId: string, state: TasksState): TasksState {
  return state.moveUndo === null ? state : { ...state, moveUndo: state.moveUndo.filter((task) => task.id !== taskId) };
}

const adapter = createEntityAdapter<Task>({ sortComparer: compareTasks });

export const initialTasksState: TasksState = adapter.getInitialState({
  projectId: null,
  loading: false,
  refreshing: false,
  loadError: null,
  saving: false,
  moveUndo: null,
  resyncAfterMove: false,
});

const { selectQueryParams } = getRouterSelectors();

export const tasksFeature = createFeature({
  name: 'tasks',
  reducer: createReducer(
    initialTasksState,
    // Another user may sign in next in this tab; nothing of the previous session may show.
    on(
      ShellActions.logoutClicked,
      AuthApiActions.sessionExpired,
      TenantActions.tenantSwitched,
      (): TasksState => initialTasksState,
    ),
    on(TasksPageActions.opened, (state, { projectId }): TasksState =>
      // Another project's tasks must not flash while this one loads; the same project's stay until the answer comes.
      state.projectId === projectId
        ? { ...state, refreshing: true, loadError: null }
        : adapter.removeAll({ ...state, projectId, loading: true, refreshing: false, loadError: null, moveUndo: null, resyncAfterMove: false }),
    ),
    on(TasksApiActions.tasksLoaded, (state, { projectId, tasks }): TasksState => {
      if (projectId !== state.projectId) {
        return state;
      }
      // A reload that lands in the middle of an own move would undo the optimistic card: keep the board, load again after.
      return state.moveUndo !== null
        ? { ...state, loading: false, refreshing: false, resyncAfterMove: true }
        : adapter.setAll(tasks, { ...state, loading: false, refreshing: false, resyncAfterMove: false });
    }),
    on(TasksApiActions.tasksLoadFailed, (state, { error }): TasksState => ({ ...state, loading: false, refreshing: false, loadError: error })),
    on(
      TasksPageActions.createSubmitted,
      TasksPageActions.updateSubmitted,
      TasksPageActions.deleteConfirmed,
      (state): TasksState => ({ ...state, saving: true }),
    ),
    on(TasksApiActions.taskSaved, (state, { task }): TasksState => adapter.upsertOne(task, { ...state, saving: false })),
    on(
      TasksApiActions.taskDeleted,
      (state, { taskId }): TasksState => adapter.removeOne(taskId, withoutUndoOf(taskId, { ...state, saving: false })),
    ),
    on(TasksApiActions.taskSaveFailed, (state): TasksState => ({ ...state, saving: false })),
    on(TasksPageActions.taskDropped, (state, { taskId, status, position }): TasksState => {
      const changed = moveTask(Object.values(state.entities) as Task[], taskId, status, position);
      if (changed.length === 0) {
        return state;
      }
      const undo = changed.map((task) => state.entities[task.id]).filter((task): task is Task => task !== undefined);
      return adapter.upsertMany(changed, { ...state, moveUndo: undo });
    }),
    // Somebody else's changes (ADR 0023). They never touch an open edit dialog: it keeps its own copy, and a save
    // over a newer version already ends in the stale-version notice.
    on(RealtimeActions.taskChanged, (state, { projectId, task }): TasksState => {
      if (projectId !== state.projectId) {
        return state;
      }
      // A task the optimistic move has changed keeps the board's version until the move settles.
      return state.moveUndo?.some((before) => before.id === task.id)
        ? { ...state, resyncAfterMove: true }
        : adapter.upsertOne(task, state);
    }),
    on(RealtimeActions.taskDeleted, (state, { projectId, taskId }): TasksState => {
      if (projectId !== state.projectId) {
        return state;
      }
      // During an own move the deleted task must not come back with an undo: take it out of the undo copy too, and
      // reload when the move settles (the neighbors' positions changed with the deletion).
      return adapter.removeOne(taskId, {
        ...withoutUndoOf(taskId, state),
        resyncAfterMove: state.moveUndo !== null ? true : state.resyncAfterMove,
      });
    }),
    on(RealtimeActions.tasksReordered, (state, { projectId, tasks }): TasksState => {
      if (projectId !== state.projectId) {
        return state;
      }
      // During an own move the positions are in flux: hold this back and reload when the move settles.
      return state.moveUndo !== null
        ? { ...state, resyncAfterMove: true }
        : adapter.updateMany(
            tasks.map(({ id, ...changes }) => ({ id, changes })),
            state,
          );
    }),
    on(TasksApiActions.taskRefreshed, (state, { task }): TasksState => adapter.upsertOne(task, state)),
    on(TasksApiActions.taskMoved, (state, { result }): TasksState =>
      adapter.updateMany(
        result.reordered.map(({ id, ...changes }) => ({ id, changes })),
        adapter.upsertOne(result.task, { ...state, moveUndo: null }),
      ),
    ),
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
