import { Injectable } from '@angular/core';

const KEY_PREFIX = 'taskflow.tenant.';

/** Remembers the tenant each user chose, in this browser. Storage can be blocked; then nothing is remembered. */
@Injectable({ providedIn: 'root' })
export class TenantStorage {
  read(userId: string): string | null {
    try {
      return localStorage.getItem(KEY_PREFIX + userId);
    } catch {
      return null;
    }
  }

  save(userId: string, tenantId: string): void {
    try {
      localStorage.setItem(KEY_PREFIX + userId, tenantId);
    } catch {
      // Not remembered; the first tenant is used next time.
    }
  }
}
