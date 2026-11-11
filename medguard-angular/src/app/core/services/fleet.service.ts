import { Injectable } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';
import { BatchDetail, Reading, Shipment } from '../models';
import { readingLevel } from '../format';
import { BatchService } from './batch.service';
import { ShipmentService } from './shipment.service';

/** Map legend states: on the road, on the road with a cold-chain breach, arrived. */
export type VehicleState = 'in_transit' | 'breach' | 'delivered';

/** One truck: a shipment plus the GPS its batch's sensor has been reporting. */
export interface FleetVehicle {
  shipment: Shipment;
  detail: BatchDetail;
  lastReading: Reading | null;
  position: [number, number];
  /** Oldest -> newest positions reported during the trip (the recent breadcrumb). */
  trail: [number, number][];
  state: VehicleState;
}

/** Deliveries stay on the map this long after arrival. */
const DELIVERED_VISIBLE_MS = 24 * 3_600_000;
const MAX_DELIVERED = 5;

/**
 * Builds the fleet from data the API already has: every in-transit shipment (plus
 * the latest deliveries) and the GPS each batch's sensor puts on its readings.
 * No vehicle table exists server-side — a truck IS its shipment's sensor.
 */
@Injectable({ providedIn: 'root' })
export class FleetService {
  constructor(
    private shipments: ShipmentService,
    private batches: BatchService,
  ) {}

  getFleet(): Observable<FleetVehicle[]> {
    return this.shipments.getShipments().pipe(
      switchMap((shipments) => {
        const now = Date.now();
        const onTheRoad = shipments.filter((s) => s.status === 'in_transit');
        const delivered = shipments
          .filter((s) => s.status === 'delivered' && s.arrivedAt && now - new Date(s.arrivedAt).getTime() < DELIVERED_VISIBLE_MS)
          .sort((a, b) => new Date(b.arrivedAt!).getTime() - new Date(a.arrivedAt!).getTime())
          .slice(0, MAX_DELIVERED);
        const tracked = [...onTheRoad, ...delivered];
        if (tracked.length === 0) return of([]);

        return forkJoin(
          tracked.map((s) =>
            this.batches.getBatchDetail(s.batchId).pipe(
              map((detail) => toVehicle(s, detail)),
              // One unreachable batch must not blank the whole map.
              catchError(() => of(null)),
            ),
          ),
        ).pipe(map((vehicles) => vehicles.filter((v): v is FleetVehicle => v !== null)));
      }),
    );
  }
}

function toVehicle(shipment: Shipment, detail: BatchDetail): FleetVehicle | null {
  const departed = shipment.departedAt ? new Date(shipment.departedAt).getTime() : 0;
  const readings = [...detail.readings]
    .filter((r) => r.lat !== 0 || r.lng !== 0) // 0,0 = reading posted without GPS
    .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime());
  const duringTrip = readings.filter((r) => new Date(r.recordedAt).getTime() >= departed);
  const fixes = duringTrip.length ? duringTrip : readings;
  if (fixes.length === 0) return null; // no GPS yet: nothing to put on a map

  const last = fixes[fixes.length - 1];
  const lastReading = detail.readings.reduce<Reading | null>(
    (latest, r) => (!latest || r.recordedAt > latest.recordedAt ? r : latest),
    null,
  );
  const openCritical = detail.alerts.some((a) => a.severity === 'critical' && !a.resolvedAt);
  const state: VehicleState =
    shipment.status === 'delivered'
      ? 'delivered'
      : detail.batch.status === 'quarantined' || openCritical || readingLevel(detail.batch, lastReading) === 'critical'
        ? 'breach'
        : 'in_transit';

  return {
    shipment,
    detail,
    lastReading,
    position: [last.lat, last.lng],
    trail: fixes.map((r) => [r.lat, r.lng] as [number, number]),
    state,
  };
}
