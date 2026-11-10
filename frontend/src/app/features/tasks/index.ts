/** Public API of the tasks feature: what other features may import. */
export { tasksFeature } from './data-access/store/tasks.feature';
export { TasksPageActions } from './data-access/store/tasks.actions';
export type { TaskPriority, TaskStatus } from './data-access/tasks-api.service';
export { TaskEditor } from './ui/task-editor';
export * as tasksEffects from './data-access/store/tasks.effects';
/** Pages are loaded on demand, so each is its own chunk (the barrel is imported by the project routes). */
export const loadBoardPage = () => import('./pages/board-page').then((m) => m.BoardPage);
export const loadTaskListPage = () => import('./pages/task-list-page').then((m) => m.TaskListPage);
