import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon, LucidePackagePlus, LucideTruck } from '@lucide/angular';

import { ShipmentService } from '../../core/services/shipment.service';
import { BatchService } from '../../core/services/batch.service';
import { Batch, Shipment } from '../../core/models';
import { shipmentStatusMeta, StatusMeta } from '../../core/status';
import { formatDateTime } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/table-row-skeleton.component';
import { SlideOverComponent } from '../../shared/components/slide-over/slide-over.component';

interface ShipmentForm {
  batchId: string;
  origin: string;
  destination: string;
  courier: string;
}

const EMPTY_FORM: ShipmentForm = { batchId: '', origin: '', destination: '', courier: '' };

@Component({
  selector: 'mg-shipments-page',
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
  templateUrl: './shipments.component.html',
})
export class ShipmentsPageComponent implements OnInit {
  shipments = signal<Shipment[]>([]);
  batches = signal<Batch[]>([]);
  loading = signal(true);
  error = signal(false);
  actionPending = signal(false);

  selected = signal<Shipment | null>(null);
  createOpen = signal(false);
  formError = signal<string | null>(null);
  form: ShipmentForm = { ...EMPTY_FORM };

  shipmentStatusMeta = shipmentStatusMeta;
  formatDateTime = formatDateTime;

  PackagePlus = LucidePackagePlus;
  Truck = LucideTruck;

  shippableBatches = computed(() => this.batches().filter((b) => b.status !== 'quarantined' && b.status !== 'recalled'));

  constructor(private shipmentService: ShipmentService, private batchService: BatchService) {}

  ngOnInit(): void {
    this.load();
    this.batchService.getBatches().subscribe((batches) => this.batches.set(batches));
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.shipmentService.getShipments().subscribe({
      next: (shipments) => {
        this.shipments.set(shipments);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  getShipmentStatusMeta(shipment: Shipment): StatusMeta {
    return shipmentStatusMeta[shipment.status];
  }

  openDetail(shipment: Shipment): void {
    this.selected.set(shipment);
  }

  closeDetail(): void {
    this.selected.set(null);
  }

  markDelivered(shipment: Shipment): void {
    this.actionPending.set(true);
    this.shipmentService.markShipmentDelivered(shipment.id).subscribe({
      next: (updated) => {
        this.shipments.update((list) => list.map((s) => (s.id === updated.id ? updated : s)));
        this.selected.set(updated);
        this.actionPending.set(false);
      },
      error: () => this.actionPending.set(false),
    });
  }

  openCreate(): void {
    this.form = { ...EMPTY_FORM };
    this.formError.set(null);
    this.createOpen.set(true);
  }

  closeCreate(): void {
    this.createOpen.set(false);
  }

  submitCreate(): void {
    const batchId = this.form.batchId;
    const origin = this.form.origin.trim();
    const destination = this.form.destination.trim();

    if (!batchId) {
      this.formError.set('Select a batch to ship.');
      return;
    }
    if (origin.length < 2 || destination.length < 2) {
      this.formError.set('Origin and destination are both required.');
      return;
    }

    this.formError.set(null);
    this.actionPending.set(true);
    this.shipmentService
      .createShipment({
        batchId,
        origin,
        destination,
        courier: this.form.courier.trim() || undefined,
      })
      .subscribe({
        next: (shipment) => {
          this.shipments.update((list) => [shipment, ...list]);
          this.batches.update((list) => list.map((b) => (b.id === batchId ? { ...b, status: 'in_transit' } : b)));
          this.actionPending.set(false);
          this.closeCreate();
        },
        error: () => {
          this.actionPending.set(false);
          this.formError.set('Could not create shipment. The batch may no longer be shippable.');
        },
      });
  }
}
