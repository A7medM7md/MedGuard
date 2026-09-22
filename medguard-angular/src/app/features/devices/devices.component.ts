import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  LucideDynamicIcon,
  LucideBatteryMedium,
  LucideCpu,
  LucidePlusCircle,
  LucideSignal,
} from '@lucide/angular';

import { DeviceService } from '../../core/services/device.service';
import { BatchService } from '../../core/services/batch.service';
import { Batch, Device } from '../../core/models';
import { deviceStatusMeta, toneText } from '../../core/status';
import { formatDateTime, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/table-row-skeleton.component';
import { SlideOverComponent } from '../../shared/components/slide-over/slide-over.component';

const DEVICE_MODELS = ['CryoTag S2', 'ThermoLink Pro', 'ColdSense M1'];

@Component({
  selector: 'mg-devices-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    LucideDynamicIcon,
    PageHeaderComponent,
    DataTableComponent,
    ColumnDirective,
    StatusBadgeComponent,
    EmptyStateComponent,
    TableRowSkeletonComponent,
    SlideOverComponent,
  ],
  templateUrl: './devices.component.html',
})
export class DevicesPageComponent implements OnInit {
  devices = signal<Device[]>([]);
  batches = signal<Batch[]>([]);
  loading = signal(true);
  error = signal(false);

  selectedDevice = signal<Device | null>(null);

  registerOpen = signal(false);
  registering = signal(false);
  formError = signal<string | null>(null);
  form = { deviceCode: '', model: DEVICE_MODELS[0], assignedBatchId: '' };

  deviceStatusMeta = deviceStatusMeta;
  toneText = toneText;
  timeAgo = timeAgo;
  formatDateTime = formatDateTime;
  deviceModels = DEVICE_MODELS;

  Cpu = LucideCpu;
  PlusCircle = LucidePlusCircle;
  BatteryMedium = LucideBatteryMedium;
  Signal = LucideSignal;

  constructor(private deviceService: DeviceService, private batchService: BatchService, private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.load();
    this.batchService.getBatches().subscribe((batches) => this.batches.set(batches));
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.deviceService.getDevices().subscribe({
      next: (devices) => {
        this.devices.set(devices);
        this.loading.set(false);

        // Deep link from the top-bar search ("jump to" a specific device).
        const targetId = this.route.snapshot.queryParamMap.get('device');
        if (targetId) {
          const target = devices.find((d) => d.id === targetId);
          if (target) this.selectedDevice.set(target);
        }
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  openDetail(device: Device): void {
    this.selectedDevice.set(device);
  }

  closeDetail(): void {
    this.selectedDevice.set(null);
  }

  getStatusMeta(device: Device) {
    return deviceStatusMeta[device.status];
  }

  unassign(device: Device): void {
    this.deviceService.unassignDevice(device.id).subscribe((updated) => {
      this.devices.update((list) => list.map((d) => (d.id === updated.id ? updated : d)));
      this.selectedDevice.set(updated);
    });
  }

  openRegister(): void {
    this.form = { deviceCode: '', model: DEVICE_MODELS[0], assignedBatchId: '' };
    this.formError.set(null);
    this.registerOpen.set(true);
  }

  closeRegister(): void {
    this.registerOpen.set(false);
  }

  submitRegister(): void {
    const deviceCode = this.form.deviceCode.trim();
    if (deviceCode.length < 3) {
      this.formError.set('Device ID must be at least 3 characters.');
      return;
    }
    if (!/^[A-Za-z0-9-]+$/.test(deviceCode)) {
      this.formError.set('Device ID may only contain letters, digits and dashes.');
      return;
    }

    this.formError.set(null);
    this.registering.set(true);
    this.deviceService.registerDevice(deviceCode, this.form.model).subscribe({
      next: (created) => {
        const batchId = this.form.assignedBatchId;
        if (!batchId) {
          this.devices.update((list) => [created, ...list]);
          this.registering.set(false);
          this.closeRegister();
          return;
        }
        this.deviceService.assignDeviceToBatch(created.id, batchId).subscribe({
          next: (assigned) => {
            this.devices.update((list) => [assigned, ...list]);
            this.registering.set(false);
            this.closeRegister();
          },
          error: () => {
            // Device is registered even though the assignment failed — surface both states.
            this.devices.update((list) => [created, ...list]);
            this.registering.set(false);
            this.formError.set(`Device ${created.deviceCode} was registered, but assigning it to the batch failed.`);
          },
        });
      },
      error: () => {
        this.registering.set(false);
        this.formError.set('Could not register device. The device ID may already be taken.');
      },
    });
  }
}
