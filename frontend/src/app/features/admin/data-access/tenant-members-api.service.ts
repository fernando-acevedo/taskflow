import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TenantRole } from '../../../core/tenant/tenant.models';

export interface TenantMember {
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly role: TenantRole;
  readonly isActive: boolean;
  readonly isInvitationPending: boolean;
  readonly joinedAt: string;
}

export interface NewMemberInput {
  readonly email: string;
  readonly role: TenantRole;
  readonly displayName?: string;
}

/** The account exists even when `invitationSent` is false; the invitation can then be resent. */
export interface AddedMember {
  readonly member: TenantMember;
  readonly accountCreated: boolean;
  readonly invitationSent: boolean;
}

/** A time the platform's super-admin opened this tenant. */
export interface SupportAccess {
  readonly id: string;
  readonly userId: string;
  readonly email: string;
  readonly displayName: string;
  readonly at: string;
}

/** The current tenant's people (`/api/tenant/...`, for its owners and admins). The tenant travels in `X-Tenant`. */
@Injectable({ providedIn: 'root' })
export class TenantMembersApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/tenant`;

  list(): Observable<TenantMember[]> {
    return this.http.get<TenantMember[]>(`${this.baseUrl}/members`);
  }

  add(member: NewMemberInput): Observable<AddedMember> {
    return this.http.post<AddedMember>(`${this.baseUrl}/members`, member);
  }

  changeRole(userId: string, role: TenantRole): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/members/${userId}`, { role });
  }

  remove(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/members/${userId}`);
  }

  resendInvitation(userId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/members/${userId}/invitation`, null);
  }

  supportAccess(): Observable<SupportAccess[]> {
    return this.http.get<SupportAccess[]>(`${this.baseUrl}/support-access`);
  }

  /** Platform-wide, super-admin only: the account can't sign in to any tenant. */
  setActive(userId: string, active: boolean): Observable<void> {
    return this.http.post<void>(
      `${environment.apiUrl}/api/admin/users/${userId}/${active ? 'activate' : 'deactivate'}`,
      null,
    );
  }
}
