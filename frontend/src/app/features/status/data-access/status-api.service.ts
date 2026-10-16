import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy';

export interface HealthReport {
  status: HealthStatus;
  totalDurationMs: number;
  checks: { name: string; status: HealthStatus; durationMs: number }[];
}

export interface SystemInfo {
  name: string;
  version: string;
  environment: string;
  culture: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class StatusApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  getHealth(): Observable<HealthReport> {
    return this.http.get<HealthReport>(`${this.baseUrl}/health`);
  }

  getInfo(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>(`${this.baseUrl}/api/system/info`);
  }
}
