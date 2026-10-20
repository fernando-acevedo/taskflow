import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Credentials, InvitationAcceptance, TokenResponse, User } from './auth.models';

export const AUTH_ENDPOINTS_PREFIX = '/api/auth/';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  login(credentials: Credentials): Observable<TokenResponse> {
    return this.http.post<TokenResponse>(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}login`, credentials);
  }

  register(credentials: Credentials): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}register`, credentials);
  }

  acceptInvitation(acceptance: InvitationAcceptance): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/invitations/accept`, acceptance);
  }

  refresh(refreshToken: string): Observable<TokenResponse> {
    return this.http.post<TokenResponse>(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}refresh`, { refreshToken });
  }

  me(): Observable<User> {
    return this.http.get<User>(`${this.baseUrl}/api/users/me`);
  }

  updateProfile(displayName: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/users/me`, { displayName });
  }
}
