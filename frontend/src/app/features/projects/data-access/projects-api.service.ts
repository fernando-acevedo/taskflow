import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PagedResult } from '../../../core/http/api-error';

export type ProjectRole = 'Owner' | 'Member';

export interface Project {
  id: string;
  name: string;
  description: string | null;
  clientId: string;
  clientName: string;
  isArchived: boolean;
  /** Null when an admin views a project they are not a member of (read-only). */
  myRole: ProjectRole | null;
  memberCount: number;
  createdAt: string;
  updatedAt: string;
  /** Base64 concurrency token; send it back when editing. */
  rowVersion: string;
}

export interface ProjectMember {
  userId: string;
  email: string;
  displayName: string;
  isActive: boolean;
  role: ProjectRole;
  joinedAt: string;
}

export interface ProjectInput {
  clientId: string;
  name: string;
  description: string | null;
}

export interface ProjectQuery {
  search: string;
  clientId: string | null;
  archived: boolean;
  page: number;
  pageSize: number;
}

export interface ClientOption {
  id: string;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class ProjectsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/projects`;

  list(query: ProjectQuery): Observable<PagedResult<Project>> {
    let params = new HttpParams({
      fromObject: { search: query.search, archived: query.archived, page: query.page, pageSize: query.pageSize },
    });
    if (query.clientId) {
      params = params.set('clientId', query.clientId);
    }
    return this.http.get<PagedResult<Project>>(this.baseUrl, { params });
  }

  get(id: string): Observable<Project> {
    return this.http.get<Project>(`${this.baseUrl}/${id}`);
  }

  create(project: ProjectInput): Observable<Project> {
    return this.http.post<Project>(this.baseUrl, project);
  }

  update(id: string, project: ProjectInput, rowVersion: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, { ...project, rowVersion });
  }

  setArchived(id: string, archived: boolean): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/${archived ? 'archive' : 'restore'}`, null);
  }

  members(id: string): Observable<ProjectMember[]> {
    return this.http.get<ProjectMember[]>(`${this.baseUrl}/${id}/members`);
  }

  addMember(id: string, email: string, role: ProjectRole): Observable<ProjectMember> {
    return this.http.post<ProjectMember>(`${this.baseUrl}/${id}/members`, { email, role });
  }

  changeRole(id: string, userId: string, role: ProjectRole): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/members/${userId}`, { role });
  }

  removeMember(id: string, userId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/members/${userId}`);
  }

  /** Active clients to choose from when creating or moving a project. */
  clientOptions(): Observable<ClientOption[]> {
    return this.http
      .get<PagedResult<ClientOption>>(`${environment.apiUrl}/api/clients`, { params: { pageSize: 100 } })
      .pipe(map((page) => page.items.map(({ id, name }) => ({ id, name }))));
  }
}
