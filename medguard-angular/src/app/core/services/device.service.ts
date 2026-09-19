import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, delay, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse, Device, DeviceDto } from '../models';
import { MOCK_DEVICES } from '../mock-data';

const DUMMY_LATENCY_MS = 350;

const DEVICE_STATUS_MAP: Record<number, Device['status']> = {
  0: 'online',
  1: 'stale',
  2: 'offline',
};

export function mapDeviceDto(dto: DeviceDto): Device {
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

@Injectable({ providedIn: 'root' })
export class DeviceService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

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
