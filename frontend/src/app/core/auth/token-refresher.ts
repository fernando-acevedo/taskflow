import { Injectable, inject } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import { AuthApiService } from './auth-api.service';
import { TokenStorage } from './token-storage';

/**
 * Refreshes the access token at most once at a time: requests that fail with 401 while a refresh is
 * running wait for that same refresh instead of starting their own.
 */
@Injectable({ providedIn: 'root' })
export class TokenRefresher {
  private readonly api = inject(AuthApiService);
  private readonly tokens = inject(TokenStorage);
  private inFlight: Observable<string> | null = null;

  refresh(): Observable<string> {
    if (this.inFlight) {
      return this.inFlight;
    }

    const refreshToken = this.tokens.refreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('There is no refresh token.'));
    }

    this.inFlight = this.api.refresh(refreshToken).pipe(
      tap((tokens) => this.tokens.save(tokens)),
      map((tokens) => tokens.accessToken),
      finalize(() => (this.inFlight = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.inFlight;
  }
}
