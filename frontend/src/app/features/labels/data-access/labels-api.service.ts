import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface Label {
  readonly id: string;
  readonly name: string;
  /** `#RRGGBB`. */
  readonly color: string;
  /** Concurrency token (base64): edits send it back, and the API answers 409 `Concurrency.Stale` if it moved. */
  readonly rowVersion: string;
}

/** What a task carries of a label: no row version, since labels are edited through the labels endpoints only. */
export type LabelSummary = Pick<Label, 'id' | 'name' | 'color'>;

export interface LabelInput {
  readonly name: string;
  readonly color: string;
}

@Injectable({ providedIn: 'root' })
export class LabelsApiService {
  private readonly http = inject(HttpClient);

  list(projectId: string): Observable<Label[]> {
    return this.http.get<Label[]>(this.url(projectId));
  }

  create(projectId: string, label: LabelInput): Observable<Label> {
    return this.http.post<Label>(this.url(projectId), label);
  }

  update(projectId: string, labelId: string, label: LabelInput, rowVersion: string): Observable<Label> {
    return this.http.put<Label>(`${this.url(projectId)}/${labelId}`, { ...label, rowVersion });
  }

  delete(projectId: string, labelId: string): Observable<void> {
    return this.http.delete<void>(`${this.url(projectId)}/${labelId}`);
  }

  private url(projectId: string): string {
    return `${environment.apiUrl}/api/projects/${projectId}/labels`;
  }
}
