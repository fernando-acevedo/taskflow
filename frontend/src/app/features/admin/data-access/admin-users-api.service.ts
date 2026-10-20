import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { User } from '../../../core/auth/auth.models';
import { PagedResult } from '../../../core/http/api-error';

export interface UserQuery {
  search: string;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class AdminUsersApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/admin/users`;

  list(query: UserQuery): Observable<PagedResult<User>> {
    const params = new HttpParams({ fromObject: { search: query.search, page: query.page, pageSize: query.pageSize } });
    return this.http.get<PagedResult<User>>(this.baseUrl, { params });
  }

  setActive(id: string, active: boolean): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/${active ? 'activate' : 'deactivate'}`, null);
  }

  setAdmin(id: string, admin: boolean): Observable<void> {
    const url = `${this.baseUrl}/${id}/admin`;
    return admin ? this.http.put<void>(url, null) : this.http.delete<void>(url);
  }
}
