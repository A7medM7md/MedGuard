import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, switchMap, timer } from 'rxjs';
import { LucideDynamicIcon, LucideActivity } from '@lucide/angular';

import { FleetService, FleetVehicle, VehicleState } from '../../core/services/fleet.service';
import { StatusTone } from '../../core/status';
import { timeAgo } from '../../core/format';
import { MapPinData, MapViewComponent } from '../../shared/components/map-view/map-view.component';

/** Trucks move slowly; every 30s is live enough without hammering a free-tier API. */
const REFRESH_MS = 30_000;

const STATE_TONE: Record<VehicleState, StatusTone> = { in_transit: 'transit', breach: 'critical', delivered: 'safe' };
const STATE_LABEL: Record<VehicleState, string> = { in_transit: 'In transit', breach: 'Breach in transit', delivered: 'Delivered' };

/**
 * Dashboard "Fleet in transit" card: every truck on the road (and today's
 * deliveries) at the position its sensor last reported, with the route it has
 * been driving, coloured by cold-chain state. Click a truck to open its batch.
 */
@Component({
  selector: 'mg-fleet-card',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon, MapViewComponent],
  template: `
    <section class="rounded-lg border border-border bg-card p-4 shadow-card">
      <header class="mb-3 grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3">
        <h2 class="flex min-w-0 items-center gap-2 text-sm font-semibold text-foreground">
          <svg [lucideIcon]="Activity" class="h-4 w-4 shrink-0 text-transit" aria-hidden="true"></svg>
          Fleet in transit
        </h2>
        <p class="numeric text-2xs text-muted-foreground">
          {{ onRoad() }} {{ onRoad() === 1 ? 'vehicle' : 'vehicles' }} tracked
          <span *ngIf="breaches() > 0" class="font-semibold text-critical"> · {{ breaches() }} breach{{ breaches() === 1 ? '' : 'es' }}</span>
        </p>
      </header>

      <div class="relative">
        <mg-map-view
          [pins]="pins()"
          [height]="300"
          [ariaLabel]="'Map showing ' + pins().length + ' tracked vehicles'"
          (pinClick)="openBatch($event)"
        />
        <div
          *ngIf="loading() || failed() || pins().length === 0"
          class="pointer-events-none absolute inset-0 z-[500] flex items-center justify-center"
        >
          <span class="rounded-md border border-border bg-card/90 px-3 py-1.5 text-2xs font-medium text-muted-foreground shadow-card">
            {{ loading() ? 'Locating vehicles…' : failed() ? 'Live positions unavailable — retrying' : 'No vehicles on the road right now' }}
          </span>
        </div>
      </div>

      <ul class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-2xs text-muted-foreground">
        <li class="flex items-center gap-1.5"><span class="h-2 w-2 rounded-full bg-transit"></span> In transit</li>
        <li class="flex items-center gap-1.5"><span class="h-2 w-2 rounded-full bg-critical"></span> Breach in transit</li>
        <li class="flex items-center gap-1.5"><span class="h-2 w-2 rounded-full bg-safe"></span> Delivered</li>
      </ul>
    </section>
  `,
})
export class FleetCardComponent implements OnInit {
  private fleet = inject(FleetService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  Activity = LucideActivity;

  vehicles = signal<FleetVehicle[]>([]);
  loading = signal(true);
  failed = signal(false);

  onRoad = computed(() => this.vehicles().filter((v) => v.state !== 'delivered').length);
  breaches = computed(() => this.vehicles().filter((v) => v.state === 'breach').length);

  pins = computed<MapPinData[]>(() =>
    this.vehicles().map((v) => {
      const { shipment, detail, lastReading } = v;
      const reading = lastReading ? `${lastReading.temperatureC.toFixed(1)} °C · ${timeAgo(lastReading.recordedAt)}` : 'No reading yet';
      return {
        id: detail.batch.id,
        lat: v.position[0],
        lng: v.position[1],
        label: `${detail.batch.batchNumber} · ${STATE_LABEL[v.state]}`,
        sublabel: detail.batch.drugName,
        details: [
          `${shipment.origin} → ${shipment.destination}`,
          shipment.courier ?? 'Courier not set',
          v.state === 'delivered' && shipment.arrivedAt ? `Delivered ${timeAgo(shipment.arrivedAt)}` : reading,
        ],
        tone: STATE_TONE[v.state],
        geofence: v.state === 'delivered' ? undefined : 12_000,
        trail: v.state === 'delivered' ? undefined : v.trail,
      };
    }),
  );

  ngOnInit(): void {
    timer(0, REFRESH_MS)
      .pipe(
        switchMap(() =>
          this.fleet.getFleet().pipe(
            catchError(() => {
              this.failed.set(true);
              this.loading.set(false);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((vehicles) => {
        this.vehicles.set(vehicles);
        this.loading.set(false);
        this.failed.set(false);
      });
  }

  openBatch(batchId: string): void {
    this.router.navigate(['/batches', batchId]);
  }
}
