import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, delay, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Alert, AlertDto, ApiResponse, PagedResultDto } from '../models';
import { MOCK_ALERTS } from '../mock-data';

const DUMMY_LATENCY_MS = 350;

// Server also has Info=0, which Batch.RecordReading never actually raises
// (only Warning/Critical) — mapped to 'warning' defensively should it occur.
const ALERT_SEVERITY_MAP: Record<number, Alert['severity']> = {
  0: 'warning',
  1: 'warning',
  2: 'critical',
};

export function mapAlertDto(dto: AlertDto): Alert {
  return {
    id: dto.id,
    batchId: dto.batchId,
    batchNumber: dto.batchNumber,
    drugName: dto.drugName,
    severity: ALERT_SEVERITY_MAP[dto.severity],
    message: dto.message,
    triggeredAt: dto.triggeredAtUtc,
    resolvedAt: dto.resolvedAtUtc,
    resolvedBy: dto.isResolved ? 'Resolved' : null,
  };
}

@Injectable({ providedIn: 'root' })
export class AlertService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getAlerts(): Observable<Alert[]> {
    if (environment.useDummyData) {
      return of(MOCK_ALERTS).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<AlertDto[]>>(`${this.baseUrl}/alerts/unresolved`)
      .pipe(map((res) => res.data.map(mapAlertDto)));
  }

  /**
   * Full alert log for the Alerts page — resolved and unresolved together.
   * Fetched as one largeish page and filtered/paginated client-side, same
   * approach as every other list page in this app (Batches, Devices, ...).
   */
  getAllAlerts(): Observable<Alert[]> {
    if (environment.useDummyData) {
      return of(MOCK_ALERTS).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<PagedResultDto<AlertDto>>>(`${this.baseUrl}/alerts`, { params: { page: 1, pageSize: 200 } })
      .pipe(map((res) => res.data.items.map(mapAlertDto)));
  }

  resolveAlert(alertId: string): Observable<void> {
    if (environment.useDummyData) {
      const alert = MOCK_ALERTS.find((a) => a.id === alertId);
      if (alert) {
        alert.resolvedAt = new Date().toISOString();
        alert.resolvedBy = 'You';
      }
      return of(undefined).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http.post<void>(`${this.baseUrl}/alerts/${alertId}/resolve`, {});
  }
}
