import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TaskPriority, TaskStatus } from '../../tasks/data-access/tasks-api.service';

export interface MyTask {
  readonly id: string;
  readonly projectId: string;
  readonly projectName: string;
  readonly clientName: string;
  readonly title: string;
  readonly status: TaskStatus;
  readonly priority: TaskPriority;
  readonly dueDate: string | null;
  readonly isOverdue: boolean;
}

export interface ClientSummary {
  readonly clientId: string;
  readonly clientName: string;
  readonly activeProjects: number;
  readonly openTasks: number;
  readonly overdueTasks: number;
}

export interface MyDashboard {
  readonly openTasks: number;
  readonly overdueTasks: number;
  readonly dueThisWeek: number;
  /** The first 50 open tasks assigned to the user, overdue first, then by due date. */
  readonly tasks: readonly MyTask[];
  readonly clients: readonly ClientSummary[];
}

export interface AssigneeLoad {
  /** Null for unassigned tasks. */
  readonly userId: string | null;
  readonly displayName: string | null;
  readonly openTasks: number;
  readonly overdueTasks: number;
}

export interface ProjectDashboard {
  readonly projectId: string;
  readonly totalTasks: number;
  readonly openTasks: number;
  readonly overdueTasks: number;
  readonly dueThisWeek: number;
  /** Every status in board order, zeros included. */
  readonly byStatus: { status: TaskStatus; count: number }[];
  /** Every priority from Low to Critical, zeros included; open tasks only. */
  readonly openByPriority: { priority: TaskPriority; count: number }[];
  readonly openByAssignee: readonly AssigneeLoad[];
}

@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api`;

  mine(): Observable<MyDashboard> {
    return this.http.get<MyDashboard>(`${this.baseUrl}/dashboard`);
  }

  project(projectId: string): Observable<ProjectDashboard> {
    return this.http.get<ProjectDashboard>(`${this.baseUrl}/projects/${projectId}/dashboard`);
  }
}
