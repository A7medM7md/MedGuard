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

/** Raw shape returned by MedGuard.API — mapped to Reading by BatchService. */
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
