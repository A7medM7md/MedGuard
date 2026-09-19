import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  LucideDynamicIcon,
  LucideArrowLeft,
  LucideBan,
  LucideCalendarClock,
  LucideFactory,
  LucidePackage,
  LucidePackagePlus,
  LucideShieldCheck,
  LucideThermometer,
} from '@lucide/angular';

import { BatchService } from '../../core/services/batch.service';
import { AlertService } from '../../core/services/alert.service';
import { ShipmentService } from '../../core/services/shipment.service';
import { Alert, Batch, BatchDetail, Reading, Shipment } from '../../core/models';
import { batchStatusMeta, readingLevelMeta, shipmentStatusMeta, StatusMeta, toneText } from '../../core/status';
import { daysUntil, formatDate, formatDateTime, readingLevel, timeAgo } from '../../core/format';

import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { AlertFeedComponent } from '../../shared/components/alert-feed/alert-feed.component';
import { TemperatureChartComponent } from '../../shared/components/temperature-chart/temperature-chart.component';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { SlideOverComponent } from '../../shared/components/slide-over/slide-over.component';

interface ShipmentForm {
  origin: string;
  destination: string;
  courier: string;
}

const EMPTY_SHIPMENT_FORM: ShipmentForm = { origin: '', destination: '', courier: '' };

@Component({
  selector: 'mg-batch-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    LucideDynamicIcon,
    DataTableComponent,
    ColumnDirective,
    StatusBadgeComponent,
    EmptyStateComponent,
    AlertFeedComponent,
    TemperatureChartComponent,
    ConfirmDialogComponent,
    SlideOverComponent,
  ],
  templateUrl: './batch-detail.component.html',
})
export class BatchDetailPageComponent implements OnInit {
  detail = signal<BatchDetail | null>(null);
  loading = signal(true);
  error = signal(false);
  actionPending = signal(false);

  confirmRecall = signal(false);
  shipmentOpen = signal(false);
  shipmentFormError = signal<string | null>(null);
  shipmentForm: ShipmentForm = { ...EMPTY_SHIPMENT_FORM };

  batchStatusMeta = batchStatusMeta;
  shipmentStatusMeta = shipmentStatusMeta;
  readingLevelMeta = readingLevelMeta;
  toneText = toneText;
  readingLevel = readingLevel;
  timeAgo = timeAgo;
  formatDate = formatDate;
  formatDateTime = formatDateTime;
  daysUntil = daysUntil;

  ArrowLeft = LucideArrowLeft;
  Ban = LucideBan;
  CalendarClock = LucideCalendarClock;
  Factory = LucideFactory;
  Package = LucidePackage;
  PackagePlus = LucidePackagePlus;
  ShieldCheck = LucideShieldCheck;
  Thermometer = LucideThermometer;

  private batchId = '';

  constructor(private route: ActivatedRoute, private batchService: BatchService, private alertService: AlertService, private shipmentService: ShipmentService) {}

  ngOnInit(): void {
    this.batchId = this.route.snapshot.paramMap.get('batchId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.batchService.getBatchDetail(this.batchId).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  getBatchStatusMeta(batch: Batch): StatusMeta {
    return batchStatusMeta[batch.status];
  }

  borderClass(status: Batch['status']): string {
    const map: Record<Batch['status'], string> = {
      active: 'border-l-safe',
      in_transit: 'border-l-transit',
      quarantined: 'border-l-critical',
      delivered: 'border-l-safe',
      recalled: 'border-l-neutralst',
    };
    return map[status];
  }

  latestReading(d: BatchDetail): Reading | null {
    return d.readings.length ? d.readings[0] : null;
  }

  reversedReadings(d: BatchDetail): Reading[] {
    return [...d.readings].reverse();
  }

  currentLevel(batch: Batch): 'safe' | 'warning' | 'critical' {
    const d = this.detail();
    return readingLevel(batch, d ? this.latestReading(d) : null);
  }

  expiringDays(batch: Batch): number {
    return daysUntil(batch.expiresAt);
  }

  isBlocked(batch: Batch): boolean {
    return batch.status === 'quarantined' || batch.status === 'recalled';
  }

  clearQuarantine(batchId: string): void {
    this.actionPending.set(true);
    this.batchService.clearQuarantine(batchId).subscribe({
      next: (batch) => {
        this.detail.update((d) => (d ? { ...d, batch } : d));
        this.actionPending.set(false);
      },
      error: () => this.actionPending.set(false),
    });
  }

  recall(): void {
    this.confirmRecall.set(false);
    this.actionPending.set(true);
    this.batchService.recallBatch(this.batchId).subscribe({
      next: (batch) => {
        this.detail.update((d) => (d ? { ...d, batch } : d));
        this.actionPending.set(false);
      },
      error: () => this.actionPending.set(false),
    });
  }

  onResolve(alertId: string): void {
    this.alertService.resolveAlert(alertId).subscribe(() => {
      this.detail.update((d) => {
        if (!d) return d;
        const alerts: Alert[] = d.alerts.map((a) =>
          a.id === alertId ? { ...a, resolvedAt: new Date().toISOString(), resolvedBy: 'You' } : a,
        );
        return { ...d, alerts };
      });
    });
  }

  openShipment(): void {
    this.shipmentForm = { ...EMPTY_SHIPMENT_FORM };
    this.shipmentFormError.set(null);
    this.shipmentOpen.set(true);
  }

  closeShipment(): void {
    this.shipmentOpen.set(false);
  }

  submitShipment(): void {
    const origin = this.shipmentForm.origin.trim();
    const destination = this.shipmentForm.destination.trim();
    if (origin.length < 2 || destination.length < 2) {
      this.shipmentFormError.set('Origin and destination are both required.');
      return;
    }

    this.shipmentFormError.set(null);
    this.actionPending.set(true);
    this.shipmentService
      .createShipment({
        batchId: this.batchId,
        origin,
        destination,
        courier: this.shipmentForm.courier.trim() || undefined,
      })
      .subscribe({
        next: (shipment) => {
          this.detail.update((d) => (d ? { ...d, shipments: [shipment, ...d.shipments], batch: { ...d.batch, status: 'in_transit' } } : d));
          this.actionPending.set(false);
          this.closeShipment();
        },
        error: () => {
          this.actionPending.set(false);
          this.shipmentFormError.set('Could not create shipment. The batch may no longer be shippable.');
        },
      });
  }

  markDelivered(shipment: Shipment): void {
    this.actionPending.set(true);
    this.shipmentService.markShipmentDelivered(shipment.id).subscribe({
      next: (updated) => {
        this.detail.update((d) => {
          if (!d) return d;
          const shipments = d.shipments.map((s) => (s.id === updated.id ? updated : s));
          return { ...d, shipments };
        });
        this.actionPending.set(false);
      },
      error: () => this.actionPending.set(false),
    });
  }
}
