import { Injectable } from '@angular/core';
import { TokenResponse } from './auth.models';

const REFRESH_TOKEN_KEY = 'taskflow.refreshToken';

/**
 * The access token lives only in memory; the refresh token is kept in localStorage so a reload can restore
 * the session. See docs/adr/0003-bearer-tokens-over-cookies.md for the trade-off.
 */
@Injectable({ providedIn: 'root' })
export class TokenStorage {
  private access: string | null = null;
  private accessExpiresAt = 0;

  accessToken(): string | null {
    return this.access;
  }

  /** Whether the access token is gone or expires within `marginMs` (the bearer token is opaque, so its lifetime is tracked here). */
  accessTokenExpiresWithin(marginMs: number): boolean {
    return this.access === null || Date.now() + marginMs >= this.accessExpiresAt;
  }

  refreshToken(): string | null {
    try {
      return localStorage.getItem(REFRESH_TOKEN_KEY);
    } catch {
      return null;
    }
  }

  save(tokens: TokenResponse): void {
    this.access = tokens.accessToken;
    this.accessExpiresAt = Date.now() + tokens.expiresIn * 1000;
    try {
      localStorage.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken);
    } catch {
      // Storage unavailable (private mode): the session still works until the tab closes.
    }
  }

  clear(): void {
    this.access = null;
    try {
      localStorage.removeItem(REFRESH_TOKEN_KEY);
    } catch {
      // Nothing stored, nothing to clear.
    }
  }
}
