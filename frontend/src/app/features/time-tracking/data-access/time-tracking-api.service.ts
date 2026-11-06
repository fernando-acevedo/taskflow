import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export const NOTE_MAX_LENGTH = 500;

export interface TimeEntry {
  readonly id: string;
  readonly taskId: string;
  readonly userId: string;
  readonly userName: string;
  /** `yyyy-MM-dd`. */
  readonly date: string;
  readonly minutes: number;
  readonly note: string | null;
  readonly createdAt: string;
}

export interface TimeEntryInput {
  readonly date: string;
  readonly minutes: number;
  readonly note: string | null;
}

export interface TaskTime {
  /** Newest date first. */
  readonly entries: readonly TimeEntry[];
  readonly totalMinutes: number;
  readonly estimatedMinutes: number | null;
}

export interface TimeFilters {
  /** `yyyy-MM-dd`. */
  readonly from: string | null;
  readonly to: string | null;
  readonly userId: string | null;
}

export const NO_TIME_FILTERS: TimeFilters = { from: null, to: null, userId: null };

export interface TimeSummary {
  readonly totalMinutes: number;
  readonly byUser: readonly { userId: string; userName: string; minutes: number }[];
  readonly byTask: readonly { taskId: string; title: string; estimatedMinutes: number | null; actualMinutes: number }[];
}

@Injectable({ providedIn: 'root' })
export class TimeTrackingApiService {
  private readonly http = inject(HttpClient);

  list(projectId: string, taskId: string): Observable<TaskTime> {
    return this.http.get<TaskTime>(this.entriesUrl(projectId, taskId));
  }

  create(projectId: string, taskId: string, entry: TimeEntryInput): Observable<TimeEntry> {
    return this.http.post<TimeEntry>(this.entriesUrl(projectId, taskId), entry);
  }

  update(projectId: string, taskId: string, entryId: string, entry: TimeEntryInput): Observable<TimeEntry> {
    return this.http.put<TimeEntry>(`${this.entriesUrl(projectId, taskId)}/${entryId}`, entry);
  }

  delete(projectId: string, taskId: string, entryId: string): Observable<void> {
    return this.http.delete<void>(`${this.entriesUrl(projectId, taskId)}/${entryId}`);
  }

  summary(projectId: string, filters: TimeFilters): Observable<TimeSummary> {
    return this.http.get<TimeSummary>(this.summaryUrl(projectId), { params: filterParams(filters) });
  }

  /** The CSV as a blob, fetched through HttpClient so that the bearer token travels with the request. */
  summaryCsv(projectId: string, filters: TimeFilters): Observable<Blob> {
    return this.http.get(`${this.summaryUrl(projectId)}.csv`, { params: filterParams(filters), responseType: 'blob' });
  }

  private entriesUrl(projectId: string, taskId: string): string {
    return `${environment.apiUrl}/api/projects/${projectId}/tasks/${taskId}/time-entries`;
  }

  private summaryUrl(projectId: string): string {
    return `${environment.apiUrl}/api/projects/${projectId}/time-summary`;
  }
}

function filterParams(filters: TimeFilters): HttpParams {
  let params = new HttpParams();
  if (filters.from) {
    params = params.set('from', filters.from);
  }
  if (filters.to) {
    params = params.set('to', filters.to);
  }
  if (filters.userId) {
    params = params.set('userId', filters.userId);
  }
  return params;
}
