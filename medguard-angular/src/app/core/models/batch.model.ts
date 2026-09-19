import { Alert, AlertDto } from './alert.model';
import { Reading, SensorReadingDto } from './reading.model';
import { Shipment, ShipmentDto } from './shipment.model';

export type BatchStatus = 'active' | 'in_transit' | 'quarantined' | 'delivered' | 'recalled';

export interface Batch {
  id: string;
  batchNumber: string;
  drugName: string;
  manufacturer: string;
  status: BatchStatus;
  minTempC: number;
  maxTempC: number;
  quantity: number;
  unit: string;
  manufacturedAt: string;
  expiresAt: string;
  deviceId: string;
  lastReading: Reading | null;
  location: { lat: number; lng: number; label: string };
}

export interface BatchDetail {
  batch: Batch;
  readings: Reading[];
  alerts: Alert[];
  shipments: Shipment[];
}

/** BatchStatus enum on the server: Active=0, InTransit=1, Delivered=2, Quarantined=3, Recalled=4. Mapped to Batch by BatchService. */
export interface BatchDto {
  id: string;
  batchNumber: string;
  drugName: string;
  manufacturerName: string;
  quantityUnits: number;
  manufacturedDateUtc: string;
  expiryDateUtc: string;
  minSafeTemperatureC: number;
  maxSafeTemperatureC: number;
  status: number;
}

export interface BatchDetailDto {
  batch: BatchDto;
  recentReadings: SensorReadingDto[];
  alerts: AlertDto[];
  shipments: ShipmentDto[];
}
