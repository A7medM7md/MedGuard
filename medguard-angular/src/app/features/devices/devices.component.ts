import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  LucideDynamicIcon,
  LucideBatteryMedium,
  LucideCpu,
  LucidePlusCircle,
  LucideSignal,
} from '@lucide/angular';

import { MedGuardApiService } from '../../core/services/medguard-api.service';
import { Batch, Device } from '../../core/models/medguard.models';
import { deviceStatusMeta, toneText } from '../../core/status';
import { formatDateTime, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/skeletons.component';
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
  template: `
    <div class="space-y-5">
      <mg-page-header
        title="Devices"
        description="Registered IoT sensors. A device that has gone silent is itself an operational risk — stale devices are flagged."
        [action]="registerAction"
      />
      <ng-template #registerAction>
        <button
          type="button"
          (click)="openRegister()"
          class="inline-flex items-center gap-1.5 rounded-md bg-primary-600 px-3 py-2 text-xs font-semibold text-white"
        >
          <svg [lucideIcon]="PlusCircle" class="h-4 w-4" aria-hidden="true"></svg>
          Register Device
        </button>
      </ng-template>

      <p *ngIf="error()" class="rounded-lg border border-critical-border bg-critical-bg/40 px-4 py-3 text-sm text-critical">
        Could not load devices. <button type="button" class="font-semibold underline" (click)="load()">Retry</button>
      </p>

      <mg-table-row-skeleton *ngIf="loading()" [columns]="7" [rows]="5" />

      <div *ngIf="!loading() && !error() && devices().length === 0" class="rounded-lg border border-border bg-card shadow-card">
        <mg-empty-state
          [icon]="Cpu"
          title="No devices registered"
          description="Pair a temperature sensor to a storage unit or vehicle to start streaming readings."
        />
      </div>

      <mg-data-table *ngIf="!loading() && !error() && devices().length > 0" [rows]="devices()" [pageSize]="12" (rowClick)="openDetail($event)">
        <ng-template mgColumn header="Device code" let-d>
          <span class="numeric text-xs font-semibold text-foreground">{{ d.deviceCode }}</span>
        </ng-template>
        <ng-template mgColumn header="Model" let-d>
          <span class="text-xs text-foreground">{{ d.model }}</span>
        </ng-template>
        <ng-template mgColumn header="Assigned batch" let-d>
          <a
            *ngIf="d.assignedBatchId; else unassigned"
            routerLink="/batches/{{ d.assignedBatchId }}"
            (click)="$event.stopPropagation()"
            class="numeric text-xs font-semibold text-primary-600 hover:underline"
          >
            {{ d.assignedBatchNumber }}
          </a>
          <ng-template #unassigned><span class="text-xs text-muted-foreground">Unassigned</span></ng-template>
        </ng-template>
        <ng-template mgColumn header="Status" let-d>
          <mg-status-badge [meta]="getStatusMeta(d)" />
        </ng-template>
        <ng-template mgColumn header="Last seen" let-d>
          <span class="numeric text-xs" [ngClass]="d.status === 'online' ? 'text-muted-foreground' : toneText[getStatusMeta(d).tone]">
            {{ timeAgo(d.lastSeenAt) }}
          </span>
        </ng-template>
        <ng-template mgColumn header="Battery" align="right" let-d>
          <span class="numeric text-xs font-semibold" [ngClass]="d.batteryPct < 20 ? toneText.critical : ''">{{ d.batteryPct }} %</span>
        </ng-template>
        <ng-template mgColumn header="Signal" align="right" let-d>
          <span class="numeric text-xs">{{ d.signalPct }} %</span>
        </ng-template>
      </mg-data-table>

      <!-- Device detail -->
      <mg-slide-over [open]="selectedDevice() !== null" [title]="selectedDevice()?.deviceCode ?? ''" [subtitle]="selectedDevice()?.model" (close)="closeDetail()">
        <ng-container *ngIf="selectedDevice() as d">
          <div class="flex items-center gap-2">
            <mg-status-badge [meta]="deviceStatusMeta[d.status]" size="lg" [pulse]="d.status === 'offline'" />
            <span class="numeric text-xs text-muted-foreground">last seen {{ formatDateTime(d.lastSeenAt) }}</span>
          </div>
          <dl class="grid grid-cols-2 gap-3">
            <div class="rounded-md border border-border bg-muted/40 px-3 py-2">
              <dt class="flex items-center gap-1.5 text-2xs font-medium tracking-wide text-muted-foreground uppercase">
                <svg [lucideIcon]="BatteryMedium" class="h-3.5 w-3.5" aria-hidden="true"></svg> Battery
              </dt>
              <dd class="numeric mt-1 text-sm font-bold text-foreground">{{ d.batteryPct }} %</dd>
            </div>
            <div class="rounded-md border border-border bg-muted/40 px-3 py-2">
              <dt class="flex items-center gap-1.5 text-2xs font-medium tracking-wide text-muted-foreground uppercase">
                <svg [lucideIcon]="Signal" class="h-3.5 w-3.5" aria-hidden="true"></svg> Signal
              </dt>
              <dd class="numeric mt-1 text-sm font-bold text-foreground">{{ d.signalPct }} %</dd>
            </div>
          </dl>

          <div *ngIf="d.assignedBatchId; else notAssigned" class="flex flex-wrap gap-2">
            <a
              routerLink="/batches/{{ d.assignedBatchId }}"
              class="inline-flex items-center gap-1.5 rounded-md bg-primary-600 px-3 py-1.5 text-xs font-semibold text-white"
            >
              Open batch {{ d.assignedBatchNumber }}
            </a>
            <button
              type="button"
              (click)="unassign(d)"
              class="rounded-md border border-border px-3 py-1.5 text-xs font-semibold text-foreground hover:bg-muted"
            >
              Unassign
            </button>
          </div>
          <ng-template #notAssigned>
            <p class="text-xs text-muted-foreground">This device is not currently assigned to a batch.</p>
          </ng-template>
        </ng-container>
      </mg-slide-over>

      <!-- Register device -->
      <mg-slide-over [open]="registerOpen()" title="Register device" subtitle="Pair a new cold-chain sensor" (close)="closeRegister()">
        <form (ngSubmit)="submitRegister()" class="space-y-5" novalidate>
          <div class="space-y-3">
            <h3 class="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Hardware</h3>
            <div>
              <label for="deviceCode" class="mb-1 block text-xs font-medium text-foreground">Device ID <span class="text-critical">*</span></label>
              <input
                id="deviceCode"
                type="text"
                [(ngModel)]="form.deviceCode"
                name="deviceCode"
                placeholder="DEV-CRYO-0142"
                class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs"
                [class.border-critical]="formError()"
              />
            </div>
            <div>
              <label for="model" class="mb-1 block text-xs font-medium text-foreground">Model <span class="text-critical">*</span></label>
              <select id="model" [(ngModel)]="form.model" name="model" class="w-full rounded-md border border-border bg-background px-3 py-2 text-xs">
                <option *ngFor="let m of deviceModels" [value]="m">{{ m }}</option>
              </select>
            </div>
          </div>

          <div class="space-y-3">
            <h3 class="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Assignment</h3>
            <div>
              <label for="assignedBatchId" class="mb-1 block text-xs font-medium text-foreground">Assigned batch</label>
              <select id="assignedBatchId" [(ngModel)]="form.assignedBatchId" name="assignedBatchId" class="w-full rounded-md border border-border bg-background px-3 py-2 text-xs">
                <option value="">Unassigned</option>
                <option *ngFor="let b of batches()" [value]="b.id">{{ b.batchNumber }} — {{ b.drugName }}</option>
              </select>
              <p class="mt-1 text-2xs text-muted-foreground">Optional — a device can be registered before it is paired.</p>
            </div>
          </div>

          <p *ngIf="formError()" class="text-xs text-critical">{{ formError() }}</p>

          <div class="flex justify-end gap-2">
            <button type="button" (click)="closeRegister()" class="rounded-md border border-border px-3 py-1.5 text-xs font-semibold text-foreground hover:bg-muted">
              Cancel
            </button>
            <button type="submit" [disabled]="registering()" class="rounded-md bg-primary-600 px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-60">
              {{ registering() ? 'Registering…' : 'Register device' }}
            </button>
          </div>
        </form>
      </mg-slide-over>
    </div>
  `,
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

  constructor(private api: MedGuardApiService) {}

  ngOnInit(): void {
    this.load();
    this.api.getBatches().subscribe((batches) => this.batches.set(batches));
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api.getDevices().subscribe({
      next: (devices) => {
        this.devices.set(devices);
        this.loading.set(false);
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
    this.api.unassignDevice(device.id).subscribe((updated) => {
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
    this.api.registerDevice(deviceCode, this.form.model).subscribe({
      next: (created) => {
        const batchId = this.form.assignedBatchId;
        if (!batchId) {
          this.devices.update((list) => [created, ...list]);
          this.registering.set(false);
          this.closeRegister();
          return;
        }
        this.api.assignDeviceToBatch(created.id, batchId).subscribe({
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
