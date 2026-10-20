import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PagedResult } from '../../../core/http/api-error';

export type ProjectRole = 'Owner' | 'Member';

export interface Project {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly clientId: string;
  readonly clientName: string;
  readonly isArchived: boolean;
  /** Null when an admin views a project they are not a member of (read-only). */
  readonly myRole: ProjectRole | null;
  readonly memberCount: number;
  readonly createdAt: string;
  readonly updatedAt: string;
  /** Base64 concurrency token; send it back when editing. */
  readonly rowVersion: string;
}

export interface ProjectMember {
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly isActive: boolean;
  readonly role: ProjectRole;
  readonly joinedAt: string;
}

export interface ProjectInput {
  readonly clientId: string;
  readonly name: string;
  readonly description: string | null;
}

export interface ProjectQuery {
  readonly search: string;
  readonly clientId: string | null;
  readonly archived: boolean;
  readonly page: number;
  readonly pageSize: number;
}

export interface ClientOption {
  readonly id: string;
  readonly name: string;
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

  /** Every active client (id and name), to choose from when creating or moving a project. */
  clientOptions(): Observable<ClientOption[]> {
    return this.http.get<ClientOption[]>(`${environment.apiUrl}/api/clients/options`);
  }
}
