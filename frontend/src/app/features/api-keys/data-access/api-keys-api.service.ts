import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export const API_KEY_SCOPES = ['Read', 'Write'] as const;
export type ApiKeyScope = (typeof API_KEY_SCOPES)[number];

export interface ApiKey {
  readonly id: string;
  readonly name: string;
  /** The visible start of the key, to recognize it. */
  readonly prefix: string;
  readonly scope: ApiKeyScope;
  readonly createdAt: string;
  readonly expiresAt: string;
  readonly revokedAt: string | null;
  readonly lastUsedAt: string | null;
  readonly isActive: boolean;
}

export interface NewApiKeyInput {
  readonly name: string;
  readonly scope: ApiKeyScope;
  /** `yyyy-MM-dd`; null for the default of 90 days. */
  readonly expiresAt: string | null;
}

/** The answer to a creation. `secret` is the whole key and is never returned again. */
export interface CreatedApiKey {
  readonly id: string;
  readonly name: string;
  readonly prefix: string;
  readonly scope: ApiKeyScope;
  readonly createdAt: string;
  readonly expiresAt: string;
  readonly secret: string;
}

@Injectable({ providedIn: 'root' })
export class ApiKeysApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/api/api-keys`;

  list(): Observable<ApiKey[]> {
    return this.http.get<ApiKey[]>(this.url);
  }

  create(input: NewApiKeyInput): Observable<CreatedApiKey> {
    return this.http.post<CreatedApiKey>(this.url, {
      name: input.name,
      scope: input.scope,
      ...(input.expiresAt ? { expiresAt: input.expiresAt } : {}),
    });
  }

  revoke(keyId: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${keyId}`);
  }
}
