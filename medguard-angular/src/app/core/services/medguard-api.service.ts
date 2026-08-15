import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, delay, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Alert, ApiResponse, Batch, Device, Shipment } from '../models/medguard.models';
import { MOCK_ALERTS, MOCK_BATCHES, MOCK_DEVICES, MOCK_SHIPMENTS } from '../mock-data';

/** Simulated network latency so loading/skeleton states are visible in dummy mode. */
const DUMMY_LATENCY_MS = 350;

/**
 * Single point of contact between the UI and data. Every feature component
 * calls THIS service — never core/mock-data.ts directly — so switching from
 * dummy data to the real MedGuard .NET API later is a change in one file.
 *
 * ---------------------------------------------------------------------------
 * TO CONNECT THE REAL BACKEND (MedGuard.API — see BatchesController,
 * SensorReadingsController, AlertsController, ShipmentsController):
 *
 *   1. Set `useDummyData: false` in src/environments/environment.ts
 *   2. Uncomment the httpClient-based implementation below each method
 *      (marked "REAL API") and delete/comment the dummy `of(...)` version
 *   3. Note the DTO shape differences to map:
 *      - BatchStatus is an int enum server-side (0=Active..4=Recalled) vs
 *        a string union here ("active".."recalled") — map it in
 *        `mapBatchDto()` below rather than scattering the mapping in
 *        components.
 *      - The .NET DTOs are flat (BatchDto has MinSafeTemperatureC /
 *        MaxSafeTemperatureC as top-level numbers) which already matches
 *        this shape closely — no nested TemperatureRange leaks through the
 *        API, that's a domain-layer-only concept.
 * ---------------------------------------------------------------------------
 */
@Injectable({ providedIn: 'root' })
export class MedGuardApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  // ------------------------------------------------------------------ Batches

  getBatches(): Observable<Batch[]> {
    // if (environment.useDummyData) {
    //   return of(MOCK_BATCHES).pipe(delay(DUMMY_LATENCY_MS));
    // }
    // REAL API: GET /api/batches -> BatchDto[]
    return this.http.get<ApiResponse<Batch[]>>(`${this.baseUrl}/batches`).pipe(map(dtos => dtos.data));
    // return of(MOCK_BATCHES); // fallback until wired
  }

  getBatchById(id: string): Observable<Batch | undefined> {
    if (environment.useDummyData) {
      return of(MOCK_BATCHES.find((b) => b.id === id)).pipe(delay(DUMMY_LATENCY_MS));
    }
    // REAL API: GET /api/batches/{id} -> BatchDto
    // return this.http.get<BatchDto>(`${this.baseUrl}/batches/${id}`).pipe(map(mapBatchDto));
    return of(MOCK_BATCHES.find((b) => b.id === id));
  }

  createBatch(payload: Partial<Batch>): Observable<Batch> {
    if (environment.useDummyData) {
      const created: Batch = {
        id: `b-${Math.random().toString(36).slice(2, 8)}`,
        batchNumber: payload.batchNumber ?? 'NEW-BATCH',
        drugName: payload.drugName ?? '',
        manufacturer: payload.manufacturer ?? '',
        status: 'active',
        minTempC: payload.minTempC ?? 2,
        maxTempC: payload.maxTempC ?? 8,
        quantity: payload.quantity ?? 0,
        unit: payload.unit ?? 'units',
        manufacturedAt: payload.manufacturedAt ?? new Date().toISOString(),
        expiresAt: payload.expiresAt ?? new Date().toISOString(),
        deviceId: payload.deviceId ?? '',
        lastReading: null,
        location: payload.location ?? { lat: 0, lng: 0, label: 'Unassigned' },
      };
      MOCK_BATCHES.unshift(created);
      return of(created).pipe(delay(DUMMY_LATENCY_MS));
    }
    // REAL API: POST /api/batches  (CreateBatchRequest -> BatchDto)
    // return this.http.post<BatchDto>(`${this.baseUrl}/batches`, {
    //   batchNumber: payload.batchNumber,
    //   drugName: payload.drugName,
    //   manufacturerName: payload.manufacturer,
    //   quantityUnits: payload.quantity,
    //   manufacturedDateUtc: payload.manufacturedAt,
    //   expiryDateUtc: payload.expiresAt,
    //   minSafeTemperatureC: payload.minTempC,
    //   maxSafeTemperatureC: payload.maxTempC,
    // }).pipe(map(mapBatchDto));
    throw new Error('createBatch: wire to real API');
  }

  // ------------------------------------------------------------------- Alerts

  getAlerts(): Observable<Alert[]> {
    if (environment.useDummyData) {
      return of(MOCK_ALERTS).pipe(delay(DUMMY_LATENCY_MS));
    }
    // REAL API: GET /api/alerts/unresolved -> AlertDto[]  (add a GetAll endpoint server-side for the full log page)
    // return this.http.get<AlertDto[]>(`${this.baseUrl}/alerts/unresolved`).pipe(map(dtos => dtos.map(mapAlertDto)));
    return of(MOCK_ALERTS);
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
    // REAL API: POST /api/alerts/{id}/resolve -> 204 No Content
    // return this.http.post<void>(`${this.baseUrl}/alerts/${alertId}/resolve`, {});
    return of(undefined);
  }

  // --------------------------------------------------------------- Shipments

  getShipments(): Observable<Shipment[]> {
    if (environment.useDummyData) {
      return of(MOCK_SHIPMENTS).pipe(delay(DUMMY_LATENCY_MS));
    }
    // REAL API: GET /api/shipments/batch/{batchId} exists server-side per-batch;
    // add a GET /api/shipments (all) endpoint for this list page, or aggregate
    // client-side across getBatches() + per-batch shipment calls.
    return of(MOCK_SHIPMENTS);
  }

  getShipmentsForBatch(batchId: string): Observable<Shipment[]> {
    if (environment.useDummyData) {
      return of(MOCK_SHIPMENTS.filter((s) => s.batchId === batchId)).pipe(delay(DUMMY_LATENCY_MS));
    }
    // REAL API: GET /api/shipments/batch/{batchId} -> ShipmentDto[]
    // return this.http.get<ShipmentDto[]>(`${this.baseUrl}/shipments/batch/${batchId}`).pipe(map(dtos => dtos.map(mapShipmentDto)));
    return of(MOCK_SHIPMENTS.filter((s) => s.batchId === batchId));
  }

  // ----------------------------------------------------------------- Devices

  getDevices(): Observable<Device[]> {
    if (environment.useDummyData) {
      return of(MOCK_DEVICES).pipe(delay(DUMMY_LATENCY_MS));
    }
    // No DeviceController exists yet on the backend — SensorReading currently
    // carries a bare DeviceId string, not a full Device entity. Add a Device
    // entity + DevicesController server-side before wiring this one up.
    return of(MOCK_DEVICES);
  }
}

// ---------------------------------------------------------------------------
// DTO mapping helpers (uncomment and use once real endpoints are wired).
// Keeping the mapping in one place means components never see the server's
// int-enum BatchStatus — they only ever see the string union used here.
// ---------------------------------------------------------------------------

// const BATCH_STATUS_MAP: Record<number, Batch['status']> = {
//   0: 'active',
//   1: 'in_transit',
//   2: 'delivered',
//   3: 'quarantined',
//   4: 'recalled',
// };
//
// function mapBatchDto(dto: BatchDto): Batch {
//   return {
//     id: dto.id,
//     batchNumber: dto.batchNumber,
//     drugName: dto.drugName,
//     manufacturer: dto.manufacturerName,
//     status: BATCH_STATUS_MAP[dto.status],
//     minTempC: dto.minSafeTemperatureC,
//     maxTempC: dto.maxSafeTemperatureC,
//     quantity: dto.quantityUnits,
//     unit: 'units',
//     manufacturedAt: dto.manufacturedDateUtc,
//     expiresAt: dto.expiryDateUtc,
//     deviceId: '',
//     lastReading: null, // fetch separately via SensorReadingsController if needed
//     location: { lat: 0, lng: 0, label: '' }, // not modeled server-side yet
//   };
// }
