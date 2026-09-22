import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, delay, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse, Shipment, ShipmentDto } from '../models';
import { MOCK_SHIPMENTS } from '../mock-data';

const DUMMY_LATENCY_MS = 350;

const SHIPMENT_STATUS_MAP: Record<number, Shipment['status']> = {
  0: 'preparing',
  1: 'in_transit',
  2: 'delivered',
  3: 'aborted',
};

export function mapShipmentDto(dto: ShipmentDto): Shipment {
  return {
    id: dto.id,
    batchId: dto.batchId,
    batchNumber: dto.batchNumber,
    origin: dto.originLocation,
    destination: dto.destinationLocation,
    courier: dto.courierName,
    status: SHIPMENT_STATUS_MAP[dto.status],
    departedAt: dto.departedAtUtc,
    arrivedAt: dto.arrivedAtUtc,
  };
}

@Injectable({ providedIn: 'root' })
export class ShipmentService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getShipments(): Observable<Shipment[]> {
    if (environment.useDummyData) {
      return of(MOCK_SHIPMENTS).pipe(delay(DUMMY_LATENCY_MS));
    }
    return this.http
      .get<ApiResponse<ShipmentDto[]>>(`${this.baseUrl}/shipments`)
      .pipe(map((res) => res.data.map(mapShipmentDto)));
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
}
