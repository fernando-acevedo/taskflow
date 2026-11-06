import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreatedTenant, NewTenantInput, Tenant, TenantSummary } from './tenant.models';

/** The call that must work before a tenant is chosen, so the interceptor sends no tenant with it. */
export const MY_TENANTS_PATH = '/api/tenants/mine';

@Injectable({ providedIn: 'root' })
export class TenantApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/tenants`;

  mine(): Observable<Tenant[]> {
    return this.http.get<Tenant[]>(`${environment.apiUrl}${MY_TENANTS_PATH}`);
  }

  /** Super-admin only. */
  list(): Observable<TenantSummary[]> {
    return this.http.get<TenantSummary[]>(this.baseUrl);
  }

  /** Super-admin only. */
  create(input: NewTenantInput): Observable<CreatedTenant> {
    return this.http.post<CreatedTenant>(this.baseUrl, input);
  }
}
