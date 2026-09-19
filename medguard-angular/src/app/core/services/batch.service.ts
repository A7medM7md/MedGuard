import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, delay, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse, Batch, BatchDetail, BatchDetailDto, BatchDto, Reading, SensorReadingDto } from '../models';
import { MOCK_BATCHES } from '../mock-data';
import { mapAlertDto } from './alert.service';
import { mapShipmentDto } from './shipment.service';

/** Simulated network latency so loading/skeleton states are visible in dummy mode. */
const DUMMY_LATENCY_MS = 350;

const BATCH_STATUS_MAP: Record<number, Batch['status']> = {
  0: 'active',
  1: 'in_transit',
  2: 'delivered',
  3: 'quarantined',
  4: 'recalled',
};

export function mapBatchDto(dto: BatchDto): Batch {
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

@Injectable({ providedIn: 'root' })
export class BatchService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

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
}
