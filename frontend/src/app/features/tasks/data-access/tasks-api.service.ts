import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export const TASK_STATUSES = ['ToDo', 'InProgress', 'Review', 'Done'] as const;
export type TaskStatus = (typeof TASK_STATUSES)[number];

export const TASK_PRIORITIES = ['Low', 'Medium', 'High', 'Critical'] as const;
export type TaskPriority = (typeof TASK_PRIORITIES)[number];

export interface Task {
  id: string;
  projectId: string;
  title: string;
  description: string | null;
  status: TaskStatus;
  priority: TaskPriority;
  assigneeId: string | null;
  /** `yyyy-MM-dd`. */
  dueDate: string | null;
  isOverdue: boolean;
  position: number;
  createdById: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  rowVersion: string;
}

export interface TaskInput {
  title: string;
  description: string | null;
  priority: TaskPriority;
  assigneeId: string | null;
  dueDate: string | null;
}

@Injectable({ providedIn: 'root' })
export class TasksApiService {
  private readonly http = inject(HttpClient);

  /** Every task of the project; the board and the list filter on the client. */
  list(projectId: string): Observable<Task[]> {
    return this.http.get<Task[]>(this.url(projectId));
  }

  create(projectId: string, task: TaskInput): Observable<Task> {
    return this.http.post<Task>(this.url(projectId), task);
  }

  update(task: Task, changes: TaskInput): Observable<Task> {
    return this.http.put<Task>(`${this.url(task.projectId)}/${task.id}`, { ...changes, rowVersion: task.rowVersion });
  }

  move(task: Task, status: TaskStatus, position: number): Observable<Task> {
    return this.http.put<Task>(`${this.url(task.projectId)}/${task.id}/position`, {
      status,
      position,
      rowVersion: task.rowVersion,
    });
  }

  delete(task: Task): Observable<void> {
    return this.http.delete<void>(`${this.url(task.projectId)}/${task.id}`);
  }

  private url(projectId: string): string {
    return `${environment.apiUrl}/api/projects/${projectId}/tasks`;
  }
}
