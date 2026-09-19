import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LucideDynamicIcon, LucideBell, LucideCheck, LucideShieldCheck } from '@lucide/angular';

import { AlertService } from '../../core/services/alert.service';
import { Alert, AlertSeverity } from '../../core/models';
import { alertSeverityMeta, StatusMeta } from '../../core/status';
import { formatDateTime, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/table-row-skeleton.component';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';

type SeverityFilter = AlertSeverity | 'all';
type StateFilter = 'all' | 'unresolved' | 'resolved';

const SEVERITY_FILTERS: { key: SeverityFilter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'critical', label: 'Critical' },
  { key: 'warning', label: 'Warning' },
];

const STATE_FILTERS: { key: StateFilter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'unresolved', label: 'Unresolved' },
  { key: 'resolved', label: 'Resolved' },
];

@Component({
  selector: 'mg-alerts-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    LucideDynamicIcon,
    PageHeaderComponent,
    DataTableComponent,
    ColumnDirective,
    StatusBadgeComponent,
    EmptyStateComponent,
    TableRowSkeletonComponent,
    ConfirmDialogComponent,
  ],
  templateUrl: './alerts.component.html',
})
export class AlertsPageComponent implements OnInit {
  alerts = signal<Alert[]>([]);
  loading = signal(true);
  error = signal(false);

  severity = signal<SeverityFilter>('all');
  state = signal<StateFilter>('all');
  selected = signal<string[]>([]);
  pendingResolve = signal<Alert | null>(null);

  severityFilters = SEVERITY_FILTERS;
  stateFilters = STATE_FILTERS;
  alertSeverityMeta = alertSeverityMeta;
  formatDateTime = formatDateTime;
  timeAgo = timeAgo;

  Bell = LucideBell;
  Check = LucideCheck;
  ShieldCheck = LucideShieldCheck;

  rows = computed(() => {
    const severity = this.severity();
    const state = this.state();
    return this.alerts()
      .filter((a) => severity === 'all' || a.severity === severity)
      .filter((a) => state === 'all' || (state === 'resolved' ? !!a.resolvedAt : !a.resolvedAt));
  });

  selectableWarnings = computed(() =>
    this.rows()
      .filter((a) => a.severity === 'warning' && !a.resolvedAt)
      .map((a) => a.id),
  );

  confirmDescription = computed(() => {
    const a = this.pendingResolve();
    return a ? `${a.batchNumber} · ${a.drugName}: "${a.message}" This can't be undone.` : '';
  });

  constructor(private alertService: AlertService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.alertService.getAllAlerts().subscribe({
      next: (alerts) => {
        this.alerts.set(alerts);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  getSeverityMeta(alert: Alert): StatusMeta {
    return alertSeverityMeta[alert.severity];
  }

  isSelected(id: string): boolean {
    return this.selected().includes(id);
  }

  toggleSelected(id: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update((ids) => (checked ? [...ids, id] : ids.filter((i) => i !== id)));
  }

  selectAllWarnings(): void {
    this.selected.set(this.selectableWarnings());
  }

  onResolve(alert: Alert): void {
    if (alert.severity === 'critical') {
      this.pendingResolve.set(alert);
      return;
    }
    this.doResolve(alert.id);
  }

  confirmResolve(): void {
    const alert = this.pendingResolve();
    if (!alert) return;
    this.doResolve(alert.id);
    this.pendingResolve.set(null);
  }

  resolveSelected(): void {
    const ids = this.selected();
    this.selected.set([]);
    ids.forEach((id) => this.doResolve(id));
  }

  private doResolve(alertId: string): void {
    this.alertService.resolveAlert(alertId).subscribe(() => {
      this.alerts.update((alerts) =>
        alerts.map((a) => (a.id === alertId ? { ...a, resolvedAt: new Date().toISOString(), resolvedBy: 'You' } : a)),
      );
    });
  }
}
