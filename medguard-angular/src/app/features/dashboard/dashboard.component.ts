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

import { MedGuardApiService } from '../../core/services/medguard-api.service';
import { Alert, Batch } from '../../core/models/medguard.models';
import { batchStatusMeta, readingLevelMeta, StatusMeta, toneText } from '../../core/status';
import { readingLevel, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { KpiCardComponent } from '../../shared/components/kpi-card/kpi-card.component';
import { KpiCardSkeletonComponent, TableRowSkeletonComponent } from '../../shared/components/skeletons/skeletons.component';
import { AlertFeedComponent } from '../../shared/components/alert-feed/alert-feed.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { MapViewComponent, MapPinData } from '../../shared/components/map-view/map-view.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

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
  ],
  template: `
    <div class="space-y-5">
      <mg-page-header
        title="Operations Dashboard"
        description="Live cold-chain status across every tracked batch, shipment and storage unit."
        [action]="newBatchAction"
      />
      <ng-template #newBatchAction>
        <a
          routerLink="/batches"
          class="inline-flex items-center gap-1.5 rounded-md bg-primary-600 px-3 py-2 text-xs font-semibold text-white"
        >
          <svg [lucideIcon]="PackagePlus" class="h-4 w-4" aria-hidden="true"></svg>
          New batch
        </a>
      </ng-template>

      <div *ngIf="noBatches()" class="rounded-lg border border-border bg-card shadow-card">
        <mg-empty-state
          [icon]="Package"
          title="No batches are being tracked yet"
          description="Register your first medication batch and pair it with an IoT sensor to start monitoring its cold-chain."
        />
      </div>

      <ng-container *ngIf="!noBatches()">
        <!-- KPI row -->
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <ng-container *ngIf="loading(); else kpis">
            <mg-kpi-card-skeleton *ngFor="let i of [1, 2, 3, 4]" />
          </ng-container>
          <ng-template #kpis>
            <mg-kpi-card label="Active batches" [value]="activeCount()" [icon]="ShieldCheck" hint="In approved storage" />
            <mg-kpi-card
              label="In transit"
              [value]="inTransitBatches().length"
              [icon]="Truck"
              tone="transit"
              [trend]="{ direction: 'up', label: '+1 vs. yesterday' }"
            />
            <mg-kpi-card
              label="Unresolved alerts"
              [value]="unresolvedAlerts().length"
              [icon]="Bell"
              [tone]="criticalAlertTone()"
              [hint]="criticalHint()"
            />
            <mg-kpi-card
              label="Quarantined"
              [value]="quarantinedCount()"
              [icon]="ShieldAlert"
              [tone]="quarantinedCount() > 0 ? 'critical' : 'none'"
              [hint]="quarantinedCount() > 0 ? 'Held pending QA review' : 'No batches held'"
            />
          </ng-template>
        </div>

        <!-- Unresolved alerts -->
        <section class="rounded-lg border border-border bg-card shadow-card">
          <header class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 border-b border-border px-4 py-3">
            <h2 class="flex min-w-0 items-center gap-2 text-sm font-semibold text-foreground">
              <svg [lucideIcon]="Bell" class="h-4 w-4 shrink-0 text-critical" aria-hidden="true"></svg>
              Unresolved alerts
              <span *ngIf="unresolvedAlerts().length > 0" class="numeric rounded-sm bg-critical px-1.5 py-0.5 text-2xs font-bold text-white">
                {{ unresolvedAlerts().length }}
              </span>
            </h2>
            <a routerLink="/alerts" class="text-2xs font-semibold text-primary-600 hover:underline">View full log</a>
          </header>

          <mg-table-row-skeleton *ngIf="alertsLoading()" [columns]="4" [rows]="3" />
          <mg-alert-feed
            *ngIf="!alertsLoading() && unresolvedAlerts().length"
            [alerts]="unresolvedAlerts()"
            (resolve)="onResolve($event)"
          />
          <mg-empty-state
            *ngIf="!alertsLoading() && !unresolvedAlerts().length"
            [icon]="ShieldCheck"
            title="All clear"
            description="No unresolved cold-chain alerts. Every tracked batch is inside its approved temperature range."
          />
        </section>

        <!-- Fleet map -->
        <!-- <section class="rounded-lg border border-border bg-card p-4 shadow-card">
          <header class="mb-3 grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3">
            <h2 class="flex min-w-0 items-center gap-2 text-sm font-semibold text-foreground">
              <svg [lucideIcon]="Activity" class="h-4 w-4 shrink-0 text-transit" aria-hidden="true"></svg>
              Fleet in transit
            </h2>
            <p class="numeric text-2xs text-muted-foreground">{{ pins().length }} vehicles tracked</p>
          </header>
          <mg-map-view [pins]="pins()" [height]="300" />
          <ul class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-2xs text-muted-foreground">
            <li class="flex items-center gap-1.5"><span class="h-2 w-2 rounded-full bg-transit"></span> In transit</li>
            <li class="flex items-center gap-1.5"><span class="h-2 w-2 rounded-full bg-critical"></span> Breach in transit</li>
            <li class="flex items-center gap-1.5"><span class="h-2 w-2 rounded-full bg-safe"></span> Delivered</li>
          </ul>
        </section> -->

        <!-- Recent batches -->
        <section>
          <h2 class="mb-2 flex items-center gap-2 text-sm font-semibold text-foreground">
            <svg [lucideIcon]="Package" class="h-4 w-4 text-muted-foreground" aria-hidden="true"></svg>
            Recent batches
          </h2>
          <mg-data-table [rows]="recentBatches()" [pageSize]="6" (rowClick)="goToBatch($event)">
            <ng-template mgColumn header="Batch #" let-b>
              <span class="numeric text-xs font-bold text-foreground">{{ b.batchNumber }}</span>
            </ng-template>
            <ng-template mgColumn header="Drug" let-b>
              <span class="text-xs text-foreground">{{ b.drugName }}</span>
            </ng-template>
              <ng-template mgColumn header="Status" let-b>
              <mg-status-badge [meta]="getBatchStatusMeta(b)" size="sm" />
            </ng-template>
            <ng-template mgColumn header="Last Reading" align="right" let-b>
              <div class="flex flex-col items-end">
                <span class="numeric text-sm font-bold" [ngClass]="toneText[readingLevelMeta[readingLevel(b, b.lastReading)].tone]">
                  {{ b.lastReading ? (b.lastReading.temperatureC | number: '1.1-1') + ' °C' : '—' }}
                </span>
                <span class="numeric text-2xs text-muted-foreground">
                  {{ b.lastReading ? timeAgo(b.lastReading.recordedAt) : 'no data' }}
                </span>
              </div>
            </ng-template>
            <ng-template mgColumn header="" align="right" let-b>
              <a routerLink="/batches/{{ b.id }}" class="text-2xs font-semibold text-primary-600 hover:underline" (click)="$event.stopPropagation()">
                Quick view
              </a>
            </ng-template>
          </mg-data-table>
        </section>
      </ng-container>
    </div>
  `,
})
export class DashboardPageComponent implements OnInit {
  batches = signal<Batch[]>([]);
  alerts = signal<Alert[]>([]);
  loading = signal(true);
  alertsLoading = signal(true);
  resolvedIds = signal<string[]>([]);

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

  unresolvedAlerts = computed(() =>
    this.alerts().filter((a) => !a.resolvedAt && !this.resolvedIds().includes(a.id)),
  );
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

  constructor(private api: MedGuardApiService, private router: Router) {}

  ngOnInit(): void {
    this.api.getBatches().subscribe((batches) => {
      this.batches.set(batches);
      this.loading.set(false);
    });
    this.api.getAlerts().subscribe((alerts) => {
      this.alerts.set(alerts);
      this.alertsLoading.set(false);
    });
  }

  onResolve(alertId: string): void {
    this.resolvedIds.update((ids) => [...ids, alertId]);
    this.api.resolveAlert(alertId).subscribe();
  }

  goToBatch(batch: Batch): void {
    this.router.navigate(['/batches', batch.id]);
  }

  getBatchStatusMeta(batch: Batch): StatusMeta {
    return batchStatusMeta[batch.status];
  }
}
