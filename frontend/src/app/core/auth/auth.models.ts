export const ADMIN_ROLE = 'Admin';

/** Body returned by the Identity API Endpoints' /login and /refresh. */
export interface TokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

export interface User {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  roles: string[];
}

export interface Credentials {
  email: string;
  password: string;
}

export type LoginErrorCode = 'invalidCredentials' | 'lockedOut' | 'tooManyAttempts' | 'unexpected';
