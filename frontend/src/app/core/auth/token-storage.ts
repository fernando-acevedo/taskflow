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

  accessToken(): string | null {
    return this.access;
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
