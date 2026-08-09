export type BatchStatus = 'active' | 'in_transit' | 'quarantined' | 'delivered' | 'recalled';
export type AlertSeverity = 'critical' | 'warning';
export type ShipmentStatus = 'scheduled' | 'in_transit' | 'delivered' | 'delayed';
export type DeviceStatus = 'online' | 'stale' | 'offline';

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

export interface ShipmentLeg {
  label: string;
  at: string;
  detail: string;
  done: boolean;
}

export interface Shipment {
  id: string;
  batchId: string;
  batchNumber: string;
  origin: string;
  destination: string;
  courier: string;
  status: ShipmentStatus;
  departedAt: string | null;
  arrivedAt: string | null;
  legs: ShipmentLeg[];
  route: { lat: number; lng: number }[];
}

export interface Device {
  id: string;
  model: string;
  assignedBatchId: string | null;
  assignedBatchNumber: string | null;
  lastSeenAt: string;
  batteryPct: number;
  signalPct: number;
  status: DeviceStatus;
}
