export type DeviceStatus = 'online' | 'stale' | 'offline';

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

/** DeviceStatus enum on the server: Online=0, Stale=1, Offline=2 (derived, not stored). Mapped to Device by DeviceService. */
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
