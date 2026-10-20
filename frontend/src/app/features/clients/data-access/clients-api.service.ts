import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PagedResult } from '../../../core/http/api-error';

export interface Client {
  id: string;
  name: string;
  contactEmail: string | null;
  notes: string | null;
  isArchived: boolean;
  activeProjectCount: number;
  createdAt: string;
}

export interface ClientInput {
  name: string;
  contactEmail: string | null;
  notes: string | null;
}

export interface ClientQuery {
  search: string;
  includeArchived: boolean;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class ClientsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/clients`;

  list(query: ClientQuery): Observable<PagedResult<Client>> {
    const params = new HttpParams({
      fromObject: { search: query.search, includeArchived: query.includeArchived, page: query.page, pageSize: query.pageSize },
    });
    return this.http.get<PagedResult<Client>>(this.baseUrl, { params });
  }

  create(client: ClientInput): Observable<Client> {
    return this.http.post<Client>(this.baseUrl, client);
  }

  update(id: string, client: ClientInput): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, client);
  }

  setArchived(id: string, archived: boolean): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/${archived ? 'archive' : 'restore'}`, null);
  }
}
