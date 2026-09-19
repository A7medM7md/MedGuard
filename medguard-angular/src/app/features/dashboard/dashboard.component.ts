import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import {
  LucideDynamicIcon,
  LucideActivity,
  LucideBell,
  LucidePackage,
  LucidePackagePlus,
  LucideShieldAlert,
  LucideShieldCheck,
  LucideTruck,
} from '@lucide/angular';

import { BatchService } from '../../core/services/batch.service';
import { AlertService } from '../../core/services/alert.service';
import { Alert, Batch } from '../../core/models';
import { batchStatusMeta, readingLevelMeta, StatusMeta, toneText } from '../../core/status';
import { readingLevel, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { KpiCardComponent } from '../../shared/components/kpi-card/kpi-card.component';
import { KpiCardSkeletonComponent } from '../../shared/components/skeletons/kpi-card-skeleton.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/table-row-skeleton.component';
import { AlertFeedComponent } from '../../shared/components/alert-feed/alert-feed.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { MapViewComponent, MapPinData } from '../../shared/components/map-view/map-view.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

@Component({
  selector: 'mg-dashboard-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    LucideDynamicIcon,
    PageHeaderComponent,
    KpiCardComponent,
    KpiCardSkeletonComponent,
    TableRowSkeletonComponent,
    AlertFeedComponent,
    EmptyStateComponent,
    // MapViewComponent,
    DataTableComponent,
    ColumnDirective,
    StatusBadgeComponent,
    ConfirmDialogComponent,
  ],
  templateUrl: './dashboard.component.html',
})
export class DashboardPageComponent implements OnInit {
  batches = signal<Batch[]>([]);
  alerts = signal<Alert[]>([]);
  loading = signal(true);
  alertsLoading = signal(true);
  pendingResolveAlert = signal<Alert | null>(null);

  batchStatusMeta = batchStatusMeta;
  readingLevelMeta = readingLevelMeta;
  toneText = toneText;
  readingLevel = readingLevel;
  timeAgo = timeAgo;

  Package = LucidePackage;
  PackagePlus = LucidePackagePlus;
  ShieldCheck = LucideShieldCheck;
  ShieldAlert = LucideShieldAlert;
  Truck = LucideTruck;
  Bell = LucideBell;
  Activity = LucideActivity;

  noBatches = computed(() => !this.loading() && this.batches().length === 0);
  activeCount = computed(() => this.batches().filter((b) => b.status === 'active').length);
  inTransitBatches = computed(() => this.batches().filter((b) => b.status === 'in_transit'));
  quarantinedCount = computed(() => this.batches().filter((b) => b.status === 'quarantined').length);
  recentBatches = computed(() => this.batches().slice(0, 6));

  // getAlerts() already calls GET /alerts/unresolved — the server is the single
  // source of truth for what's resolved, so no client-side masking is needed
  // here (that was the bug: resolving only hid the row locally instead of
  // persisting, so other pages still saw the alert as open).
  unresolvedAlerts = computed(() => this.alerts());
  criticalAlertCount = computed(() => this.unresolvedAlerts().filter((a) => a.severity === 'critical').length);
  criticalAlertTone = computed(() =>
    this.criticalAlertCount() > 0 ? 'critical' : this.unresolvedAlerts().length > 0 ? 'warning' : 'none',
  );
  criticalHint = computed(() =>
    this.criticalAlertCount() > 0 ? `${this.criticalAlertCount()} critical need action now` : 'Nothing needs action',
  );

  pins = computed<MapPinData[]>(() =>
    this.inTransitBatches().map((b) => ({
      id: b.id,
      lat: b.location.lat,
      lng: b.location.lng,
      label: b.batchNumber,
      sublabel: `${b.drugName} · ${b.lastReading?.temperatureC.toFixed(1) ?? '—'} °C`,
      tone: readingLevel(b, b.lastReading) === 'critical' ? 'critical' : batchStatusMeta[b.status].tone,
      geofence: 25000,
    })),
  );

  confirmDescription = computed(() => {
    const a = this.pendingResolveAlert();
    return a
      ? `${a.batchNumber} · ${a.drugName}: "${a.message}" This can't be undone — the alert will be marked resolved across the app.`
      : '';
  });

  constructor(private batchService: BatchService, private alertService: AlertService, private router: Router) {}

  ngOnInit(): void {
    this.batchService.getBatches().subscribe((batches) => {
      this.batches.set(batches);
      this.loading.set(false);
    });
    this.alertService.getAlerts().subscribe((alerts) => {
      this.alerts.set(alerts);
      this.alertsLoading.set(false);
    });
  }

  onResolve(alertId: string): void {
    const alert = this.alerts().find((a) => a.id === alertId);
    if (!alert) return;

    if (alert.severity === 'critical') {
      this.pendingResolveAlert.set(alert);
      return;
    }
    this.doResolve(alert);
  }

  confirmResolve(): void {
    const alert = this.pendingResolveAlert();
    if (!alert) return;
    this.doResolve(alert);
    this.pendingResolveAlert.set(null);
  }

  cancelResolve(): void {
    this.pendingResolveAlert.set(null);
  }

  private doResolve(alert: Alert): void {
    this.alertService.resolveAlert(alert.id).subscribe(() => {
      this.alerts.update((alerts) => alerts.filter((a) => a.id !== alert.id));
    });
  }

  goToBatch(batch: Batch): void {
    this.router.navigate(['/batches', batch.id]);
  }

  getBatchStatusMeta(batch: Batch): StatusMeta {
    return batchStatusMeta[batch.status];
  }
}
