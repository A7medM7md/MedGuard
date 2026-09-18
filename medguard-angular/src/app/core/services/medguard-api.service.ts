import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, delay, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Alert,
  AlertDto,
  ApiResponse,
  Batch,
  BatchDetail,
  BatchDetailDto,
  BatchDto,
  Device,
  DeviceDto,
  Reading,
  SensorReadingDto,
  Shipment,
  ShipmentDto,
} from '../models/medguard.models';
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
    if (environment.useDummyData) {
      return of(MOCK_BATCHES).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<BatchDto[]>>(`${this.baseUrl}/batches`)
      .pipe(map((res) => res.data.map(mapBatchDto)));
  }

  getBatchById(id: string): Observable<Batch | undefined> {
    if (environment.useDummyData) {
      return of(MOCK_BATCHES.find((b) => b.id === id)).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<BatchDto>>(`${this.baseUrl}/batches/${id}`)
      .pipe(map((res) => mapBatchDto(res.data)));
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
    return this.http
      .post<ApiResponse<BatchDto>>(`${this.baseUrl}/batches`, {
        batchNumber: payload.batchNumber,
        drugName: payload.drugName,
        manufacturerName: payload.manufacturer,
        quantityUnits: payload.quantity,
        manufacturedDateUtc: payload.manufacturedAt,
        expiryDateUtc: payload.expiresAt,
        minSafeTemperatureC: payload.minTempC,
        maxSafeTemperatureC: payload.maxTempC,
      })
      .pipe(map((res) => mapBatchDto(res.data)));
  }

  getBatchDetail(id: string): Observable<BatchDetail> {
    return this.http.get<ApiResponse<BatchDetailDto>>(`${this.baseUrl}/batches/${id}/detail`).pipe(
      map((res) => ({
        batch: mapBatchDto(res.data.batch),
        readings: res.data.recentReadings.map(mapReadingDto),
        alerts: res.data.alerts.map(mapAlertDto),
        shipments: res.data.shipments.map(mapShipmentDto),
      })),
    );
  }

  clearQuarantine(batchId: string): Observable<Batch> {
    return this.http
      .post<ApiResponse<BatchDto>>(`${this.baseUrl}/batches/${batchId}/clear-quarantine`, {})
      .pipe(map((res) => mapBatchDto(res.data)));
  }

  recallBatch(batchId: string): Observable<Batch> {
    return this.http
      .post<ApiResponse<BatchDto>>(`${this.baseUrl}/batches/${batchId}/recall`, {})
      .pipe(map((res) => mapBatchDto(res.data)));
  }

  // ------------------------------------------------------------------- Alerts

  getAlerts(): Observable<Alert[]> {
    if (environment.useDummyData) {
      return of(MOCK_ALERTS).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<AlertDto[]>>(`${this.baseUrl}/alerts/unresolved`)
      .pipe(map((res) => res.data.map(mapAlertDto)));
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
    return this.http
      .get<ApiResponse<ShipmentDto[]>>(`${this.baseUrl}/shipments/batch/${batchId}`)
      .pipe(map((res) => res.data.map(mapShipmentDto)));
  }

  createShipment(payload: {
    batchId: string;
    origin: string;
    destination: string;
    courier?: string;
  }): Observable<Shipment> {
    return this.http
      .post<ApiResponse<ShipmentDto>>(`${this.baseUrl}/shipments`, {
        batchId: payload.batchId,
        originLocation: payload.origin,
        destinationLocation: payload.destination,
        courierName: payload.courier || null,
      })
      .pipe(map((res) => mapShipmentDto(res.data)));
  }

  markShipmentDelivered(shipmentId: string): Observable<Shipment> {
    return this.http
      .post<ApiResponse<ShipmentDto>>(`${this.baseUrl}/shipments/${shipmentId}/deliver`, {})
      .pipe(map((res) => mapShipmentDto(res.data)));
  }

  // ----------------------------------------------------------------- Devices

  getDevices(): Observable<Device[]> {
    if (environment.useDummyData) {
      return of(MOCK_DEVICES).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<DeviceDto[]>>(`${this.baseUrl}/devices`)
      .pipe(map((res) => res.data.map(mapDeviceDto)));
  }

  registerDevice(deviceCode: string, model: string): Observable<Device> {
    if (environment.useDummyData) {
      const created: Device = {
        id: deviceCode,
        deviceCode,
        model,
        assignedBatchId: null,
        assignedBatchNumber: null,
        lastSeenAt: new Date().toISOString(),
        batteryPct: 100,
        signalPct: 100,
        status: 'online',
      };
      MOCK_DEVICES.unshift(created);
      return of(created).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .post<ApiResponse<DeviceDto>>(`${this.baseUrl}/devices`, { deviceCode, model })
      .pipe(map((res) => mapDeviceDto(res.data)));
  }

  assignDeviceToBatch(deviceId: string, batchId: string): Observable<Device> {
    if (environment.useDummyData) {
      const device = MOCK_DEVICES.find((d) => d.id === deviceId);
      if (device) device.assignedBatchId = batchId;
      return of(device as Device).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .post<ApiResponse<DeviceDto>>(`${this.baseUrl}/devices/${deviceId}/assign/${batchId}`, {})
      .pipe(map((res) => mapDeviceDto(res.data)));
  }

  unassignDevice(deviceId: string): Observable<Device> {
    if (environment.useDummyData) {
      const device = MOCK_DEVICES.find((d) => d.id === deviceId);
      if (device) device.assignedBatchId = null;
      return of(device as Device).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .post<ApiResponse<DeviceDto>>(`${this.baseUrl}/devices/${deviceId}/unassign`, {})
      .pipe(map((res) => mapDeviceDto(res.data)));
  }
}

// ---------------------------------------------------------------------------
// DTO mapping helpers. Keeping the mapping in one place means components
// never see the server's int-enum BatchStatus/AlertSeverity — they only ever
// see the string unions used above.
// ---------------------------------------------------------------------------

const BATCH_STATUS_MAP: Record<number, Batch['status']> = {
  0: 'active',
  1: 'in_transit',
  2: 'delivered',
  3: 'quarantined',
  4: 'recalled',
};

// Server also has Info=0, which Batch.RecordReading never actually raises
// (only Warning/Critical) — mapped to 'warning' defensively should it occur.
const ALERT_SEVERITY_MAP: Record<number, Alert['severity']> = {
  0: 'warning',
  1: 'warning',
  2: 'critical',
};

function mapBatchDto(dto: BatchDto): Batch {
  return {
    id: dto.id,
    batchNumber: dto.batchNumber,
    drugName: dto.drugName,
    manufacturer: dto.manufacturerName,
    status: BATCH_STATUS_MAP[dto.status],
    minTempC: dto.minSafeTemperatureC,
    maxTempC: dto.maxSafeTemperatureC,
    quantity: dto.quantityUnits,
    unit: 'units',
    manufacturedAt: dto.manufacturedDateUtc,
    expiresAt: dto.expiryDateUtc,
    deviceId: '',
    lastReading: null, // not exposed on the list DTO yet — fetch batch detail for full history
    location: { lat: 0, lng: 0, label: '' }, // not modeled server-side yet
  };
}

function mapAlertDto(dto: AlertDto): Alert {
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

const DEVICE_STATUS_MAP: Record<number, Device['status']> = {
  0: 'online',
  1: 'stale',
  2: 'offline',
};

function mapDeviceDto(dto: DeviceDto): Device {
  return {
    id: dto.id,
    deviceCode: dto.deviceCode,
    model: dto.model,
    assignedBatchId: dto.assignedBatchId,
    assignedBatchNumber: dto.assignedBatchNumber,
    lastSeenAt: dto.lastSeenAtUtc,
    batteryPct: dto.batteryPercent,
    signalPct: dto.signalPercent,
    status: DEVICE_STATUS_MAP[dto.status],
  };
}

function mapReadingDto(dto: SensorReadingDto): Reading {
  return {
    id: dto.id,
    batchId: dto.batchId,
    deviceId: dto.deviceId,
    temperatureC: dto.temperatureC,
    humidityPct: dto.humidityPercent ?? 0,
    lat: dto.latitude ?? 0,
    lng: dto.longitude ?? 0,
    recordedAt: dto.recordedAtUtc,
  };
}

const SHIPMENT_STATUS_MAP: Record<number, Shipment['status']> = {
  0: 'preparing',
  1: 'in_transit',
  2: 'delivered',
  3: 'aborted',
};

function mapShipmentDto(dto: ShipmentDto): Shipment {
  return {
    id: dto.id,
    batchId: dto.batchId,
    origin: dto.originLocation,
    destination: dto.destinationLocation,
    courier: dto.courierName,
    status: SHIPMENT_STATUS_MAP[dto.status],
    departedAt: dto.departedAtUtc,
    arrivedAt: dto.arrivedAtUtc,
  };
}
