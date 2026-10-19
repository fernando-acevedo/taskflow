import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TaskPriority, TaskStatus } from '../../tasks/data-access/tasks-api.service';

export interface MyTask {
  id: string;
  projectId: string;
  projectName: string;
  clientName: string;
  title: string;
  status: TaskStatus;
  priority: TaskPriority;
  dueDate: string | null;
  isOverdue: boolean;
}

export interface ClientSummary {
  clientId: string;
  clientName: string;
  activeProjects: number;
  openTasks: number;
  overdueTasks: number;
}

export interface MyDashboard {
  openTasks: number;
  overdueTasks: number;
  dueThisWeek: number;
  /** The first 50 open tasks assigned to the user, overdue first, then by due date. */
  tasks: MyTask[];
  clients: ClientSummary[];
}

export interface AssigneeLoad {
  /** Null for unassigned tasks. */
  userId: string | null;
  displayName: string | null;
  openTasks: number;
  overdueTasks: number;
}

export interface ProjectDashboard {
  projectId: string;
  totalTasks: number;
  openTasks: number;
  overdueTasks: number;
  dueThisWeek: number;
  /** Every status in board order, zeros included. */
  byStatus: { status: TaskStatus; count: number }[];
  /** Every priority from Low to Critical, zeros included; open tasks only. */
  openByPriority: { priority: TaskPriority; count: number }[];
  openByAssignee: AssigneeLoad[];
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
