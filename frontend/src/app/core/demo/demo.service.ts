import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface DemoAccount {
  email: string;
  password: string;
  role: string;
}

export interface DemoStatus {
  enabled: boolean;
  nextResetAt: string | null;
  accounts: DemoAccount[];
}

const DISABLED: DemoStatus = { enabled: false, nextResetAt: null, accounts: [] };

/** Public-demo status, fetched once. A failed call counts as "not a demo" and shows nothing. */
@Injectable({ providedIn: 'root' })
export class DemoService {
  readonly status = toSignal(
    inject(HttpClient)
      .get<DemoStatus>(`${environment.apiUrl}/api/demo/status`)
      .pipe(catchError(() => of(DISABLED))),
    { initialValue: DISABLED },
  );
}
