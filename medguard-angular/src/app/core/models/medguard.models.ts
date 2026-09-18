export type BatchStatus = 'active' | 'in_transit' | 'quarantined' | 'delivered' | 'recalled';
export type AlertSeverity = 'critical' | 'warning';
export type ShipmentStatus = 'preparing' | 'in_transit' | 'delivered' | 'aborted';
export type DeviceStatus = 'online' | 'stale' | 'offline';

// ApiResponse<T> is a generic interface that represents the structure of an API response. It contains a single property, data, which holds the actual data returned from the API. The type of data is determined by the generic type parameter T, allowing for flexibility in specifying the expected data type for different API responses.
export interface ApiResponse<T> {
  succeeded: boolean;
  statusCode: number;
  message: string;
  errors: string[];
  meta: string | null;
  data: T;
}

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

export interface Reading {
  id: string;
  batchId: string;
  deviceId: string;
  temperatureC: number;
  humidityPct: number;
  lat: number;
  lng: number;
  recordedAt: string;
}

export interface Alert {
  id: string;
  batchId: string;
  batchNumber: string;
  drugName: string;
  severity: AlertSeverity;
  message: string;
  triggeredAt: string;
  resolvedAt: string | null;
  resolvedBy: string | null;
}

export interface Shipment {
  id: string;
  batchId: string;
  origin: string;
  destination: string;
  courier: string | null;
  status: ShipmentStatus;
  departedAt: string | null;
  arrivedAt: string | null;
}

export interface BatchDetail {
  batch: Batch;
  readings: Reading[];
  alerts: Alert[];
  shipments: Shipment[];
}

export interface Device {
  /** Server-side GUID — used for all API calls (assign/unassign/heartbeat routes). */
  id: string;
  /** Human-readable hardware code (e.g. "DEV-CRYO-0142") — what the UI displays. */
  deviceCode: string;
  model: string;
  assignedBatchId: string | null;
  assignedBatchNumber: string | null;
  lastSeenAt: string;
  batteryPct: number;
  signalPct: number;
  status: DeviceStatus;
}

// ---------------------------------------------------------------------------
// Raw shapes returned by MedGuard.API, as JSON (camelCase, int enums). Mapped
// to the UI-facing interfaces above by medguard-api.service.ts — components
// never see these directly.
// ---------------------------------------------------------------------------

/** BatchStatus enum on the server: Active=0, InTransit=1, Delivered=2, Quarantined=3, Recalled=4. */
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

/** AlertSeverity enum on the server: Info=0, Warning=1, Critical=2. */
export interface AlertDto {
  id: string;
  batchId: string;
  batchNumber: string;
  drugName: string;
  severity: number;
  message: string;
  triggeredAtUtc: string;
  isResolved: boolean;
  resolvedAtUtc: string | null;
}

/** DeviceStatus enum on the server: Online=0, Stale=1, Offline=2 (derived, not stored). */
export interface DeviceDto {
  id: string;
  deviceCode: string;
  model: string;
  assignedBatchId: string | null;
  assignedBatchNumber: string | null;
  lastSeenAtUtc: string;
  batteryPercent: number;
  signalPercent: number;
  status: number;
}

export interface SensorReadingDto {
  id: string;
  batchId: string;
  deviceId: string;
  temperatureC: number;
  humidityPercent: number | null;
  latitude: number | null;
  longitude: number | null;
  recordedAtUtc: string;
}

/** ShipmentStatus enum on the server: Preparing=0, InTransit=1, Delivered=2, Aborted=3. */
export interface ShipmentDto {
  id: string;
  batchId: string;
  originLocation: string;
  destinationLocation: string;
  courierName: string | null;
  status: number;
  departedAtUtc: string | null;
  arrivedAtUtc: string | null;
}

export interface BatchDetailDto {
  batch: BatchDto;
  recentReadings: SensorReadingDto[];
  alerts: AlertDto[];
  shipments: ShipmentDto[];
}
