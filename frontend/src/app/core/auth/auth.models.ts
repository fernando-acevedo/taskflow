export const ADMIN_ROLE = 'Admin';

/** Body returned by the Identity API Endpoints' /login and /refresh. */
export interface TokenResponse {
  readonly tokenType: string;
  readonly accessToken: string;
  readonly expiresIn: number;
  readonly refreshToken: string;
}

export interface User {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly isActive: boolean;
  readonly roles: readonly string[];
  /** Created by an admin and hasn't chosen a password yet. */
  readonly isInvitationPending: boolean;
}

export interface InvitationAcceptance {
  readonly email: string;
  readonly token: string;
  readonly password: string;
}

export interface Credentials {
  readonly email: string;
  readonly password: string;
}

export type LoginErrorCode = 'invalidCredentials' | 'lockedOut' | 'tooManyAttempts' | 'unexpected';
