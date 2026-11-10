import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export const ACTIVITY_PAGE_SIZE = 50;

/** What an entry's `changes` hold depends on its type; absent values are missing, not null. */
export type ActivityChanges = Readonly<Record<string, unknown>>;

export interface ActivityEntry {
  readonly id: string;
  readonly taskId: string | null;
  readonly actorId: string;
  readonly actorName: string;
  readonly type: string;
  readonly occurredAt: string;
  readonly changes: ActivityChanges;
  /** True when the change was made with an API key. */
  readonly viaApiKey: boolean;
}

/** One page of a feed; `nextCursor` is null on the last one (ADR 0025). */
export interface ActivityPage {
  readonly items: readonly ActivityEntry[];
  readonly nextCursor: string | null;
}

@Injectable({ providedIn: 'root' })
export class ActivityApiService {
  private readonly http = inject(HttpClient);

  /** Newest first, from the start or after `cursor`. With a task id, only that task's history. */
  list(projectId: string, taskId: string | null, cursor: string | null): Observable<ActivityPage> {
    const base = `${environment.apiUrl}/api/projects/${projectId}`;
    const url = taskId === null ? `${base}/activity` : `${base}/tasks/${taskId}/activity`;
    let params = new HttpParams().set('pageSize', ACTIVITY_PAGE_SIZE);
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    return this.http.get<ActivityPage>(url, { params });
  }
}
