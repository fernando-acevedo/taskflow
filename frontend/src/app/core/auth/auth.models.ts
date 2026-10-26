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
  /** Identity sends a password reset only to a confirmed email. Invited users are confirmed from the start. */
  readonly isEmailConfirmed: boolean;
}

export interface InvitationAcceptance {
  readonly email: string;
  readonly token: string;
  readonly password: string;
}

/** Body of the Identity API Endpoints' /resetPassword; the code comes from the emailed link. */
export interface PasswordReset {
  readonly email: string;
  readonly resetCode: string;
  readonly newPassword: string;
}

/** From the link emailed on registration. Not called `code`, which the DevTools sanitizer can't mask (error codes). */
export interface EmailConfirmation {
  readonly userId: string;
  readonly confirmationCode: string;
}

export type EmailConfirmationStatus = 'idle' | 'confirmed' | 'invalid' | 'unexpected';

export interface Credentials {
  readonly email: string;
  readonly password: string;
}

export type LoginErrorCode = 'invalidCredentials' | 'lockedOut' | 'tooManyAttempts' | 'unexpected';
