import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export const NOTIFICATION_TYPES = ['TaskAssigned', 'Mentioned', 'CommentOnYourTask', 'TaskDueSoon'] as const;
export type NotificationType = (typeof NOTIFICATION_TYPES)[number];

/** The values for the sentence, as they were when the notification was raised (ADR 0010). */
export interface NotificationData {
  readonly taskTitle?: string;
  readonly projectName?: string;
  readonly actorName?: string;
  readonly commentId?: string | null;
  /** `yyyy-MM-dd`, for TaskDueSoon. */
  readonly dueDate?: string;
}

export interface AppNotification {
  readonly id: string;
  readonly type: NotificationType;
  readonly projectId: string;
  /** May name a task that has since been deleted. */
  readonly taskId: string | null;
  readonly actorId: string | null;
  readonly data: NotificationData;
  readonly createdAt: string;
  readonly readAt: string | null;
}

export interface NotificationPreferences {
  readonly emailOnAssigned: boolean;
  readonly emailOnMentioned: boolean;
  readonly emailOnCommentOnMyTask: boolean;
  readonly emailDailyDigest: boolean;
}

/** One page of the list, newest first; `nextCursor` is null on the last one (ADR 0025). */
export interface NotificationsPage {
  readonly items: readonly AppNotification[];
  readonly nextCursor: string | null;
}

export const NOTIFICATIONS_PAGE_SIZE = 20;

/** How often the unread count is polled while signed in (ADR 0010). */
export const POLL_INTERVAL_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class NotificationsApiService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/api/notifications`;

  /** From the start, or after `cursor`. */
  list(cursor: string | null = null, pageSize = NOTIFICATIONS_PAGE_SIZE): Observable<NotificationsPage> {
    let params = new HttpParams().set('pageSize', pageSize);
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    return this.http.get<NotificationsPage>(this.url, { params });
  }

  unreadCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.url}/unread-count`);
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`${this.url}/${id}/read`, null);
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.url}/read-all`, null);
  }

  preferences(): Observable<NotificationPreferences> {
    return this.http.get<NotificationPreferences>(`${this.url}/preferences`);
  }

  updatePreferences(preferences: NotificationPreferences): Observable<NotificationPreferences> {
    return this.http.put<NotificationPreferences>(`${this.url}/preferences`, preferences);
  }
}
