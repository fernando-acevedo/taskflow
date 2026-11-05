import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export const COMMENT_MAX_LENGTH = 4000;

export interface TaskComment {
  readonly id: string;
  readonly taskId: string;
  readonly authorId: string;
  readonly authorName: string;
  readonly body: string;
  readonly createdAt: string;
  readonly editedAt: string | null;
  readonly isEdited: boolean;
  readonly mentionedUserIds: readonly string[];
}

export interface CommentInput {
  readonly body: string;
  readonly mentionedUserIds: readonly string[] | null;
}

@Injectable({ providedIn: 'root' })
export class CommentsApiService {
  private readonly http = inject(HttpClient);

  /** Oldest first. */
  list(projectId: string, taskId: string): Observable<TaskComment[]> {
    return this.http.get<TaskComment[]>(this.url(projectId, taskId));
  }

  create(projectId: string, taskId: string, comment: CommentInput): Observable<TaskComment> {
    return this.http.post<TaskComment>(this.url(projectId, taskId), comment);
  }

  update(projectId: string, taskId: string, commentId: string, comment: CommentInput): Observable<TaskComment> {
    return this.http.put<TaskComment>(`${this.url(projectId, taskId)}/${commentId}`, comment);
  }

  delete(projectId: string, taskId: string, commentId: string): Observable<void> {
    return this.http.delete<void>(`${this.url(projectId, taskId)}/${commentId}`);
  }

  private url(projectId: string, taskId: string): string {
    return `${environment.apiUrl}/api/projects/${projectId}/tasks/${taskId}/comments`;
  }
}
