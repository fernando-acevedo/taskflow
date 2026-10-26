import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Credentials, EmailConfirmation, InvitationAcceptance, PasswordReset, TokenResponse, User } from './auth.models';

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

  /** Identity's endpoint behind the emailed confirmation link: 200 (with a plain-text body) or 401. */
  confirmEmail(confirmation: EmailConfirmation): Observable<void> {
    const params = { userId: confirmation.userId, code: confirmation.confirmationCode };
    return this.http
      .get(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}confirmEmail`, { params, responseType: 'text' })
      .pipe(map(() => undefined));
  }

  /** Emails a new confirmation link. Like forgotPassword, it answers 200 whether or not the account exists. */
  resendConfirmationEmail(email: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}resendConfirmationEmail`, { email });
  }

  /** Answers 200 whether or not the account exists; the link is emailed only when it does. */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}forgotPassword`, { email });
  }

  resetPassword(reset: PasswordReset): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}${AUTH_ENDPOINTS_PREFIX}resetPassword`, reset);
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
