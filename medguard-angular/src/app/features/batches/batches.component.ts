import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon, LucidePackage, LucidePackagePlus, LucideSearch } from '@lucide/angular';

import { MedGuardApiService } from '../../core/services/medguard-api.service';
import { Batch, BatchStatus } from '../../core/models/medguard.models';
import { batchStatusMeta, readingLevelMeta, StatusMeta, toneText } from '../../core/status';
import { daysUntil, formatDate, readingLevel, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/skeletons.component';
import { SlideOverComponent } from '../../shared/components/slide-over/slide-over.component';

type StatusFilter = BatchStatus | 'all';

const FILTERS: { key: StatusFilter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'active', label: 'Active' },
  { key: 'in_transit', label: 'In Transit' },
  { key: 'quarantined', label: 'Quarantined' },
  { key: 'delivered', label: 'Delivered' },
  { key: 'recalled', label: 'Recalled' },
];

interface BatchForm {
  batchNumber: string;
  drugName: string;
  manufacturer: string;
  minTempC: string;
  maxTempC: string;
  quantity: string;
  manufacturedAt: string;
  expiresAt: string;
}

const EMPTY_FORM: BatchForm = {
  batchNumber: '',
  drugName: '',
  manufacturer: '',
  minTempC: '2',
  maxTempC: '8',
  quantity: '',
  manufacturedAt: '',
  expiresAt: '',
};

@Component({
  selector: 'mg-batches-page',
  standalone: true,
  imports: [
    CommonModule,
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
    <div class="space-y-4">
      <mg-page-header
        title="Batches"
        description="Every medication batch under cold-chain surveillance."
        [action]="newBatchAction"
      />
      <ng-template #newBatchAction>
        <button
          type="button"
          (click)="openNew()"
          class="inline-flex items-center gap-1.5 rounded-md bg-primary-600 px-3 py-2 text-xs font-semibold text-white"
        >
          <svg [lucideIcon]="PackagePlus" class="h-4 w-4" aria-hidden="true"></svg>
          New Batch
        </button>
      </ng-template>

      <p *ngIf="error()" class="rounded-lg border border-critical-border bg-critical-bg/40 px-4 py-3 text-sm text-critical">
        Could not load batches. <button type="button" class="font-semibold underline" (click)="load()">Retry</button>
      </p>

      <div class="flex flex-wrap items-center gap-2">
        <div class="flex flex-wrap gap-1" role="group" aria-label="Filter by status">
          <button
            *ngFor="let f of filters"
            type="button"
            (click)="filter.set(f.key)"
            [attr.aria-pressed]="filter() === f.key"
            class="rounded-sm border px-2.5 py-1.5 text-2xs font-semibold"
            [ngClass]="filter() === f.key ? 'border-primary-600 bg-primary-100 text-primary-900' : 'border-border bg-card text-muted-foreground hover:text-foreground'"
          >
            {{ f.label }}
          </button>
        </div>
        <div class="relative min-w-0 flex-1 sm:max-w-xs">
          <svg [lucideIcon]="Search" class="pointer-events-none absolute top-1/2 left-2.5 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true"></svg>
          <input
            [(ngModel)]="query"
            placeholder="Batch number or drug…"
            aria-label="Search batches"
            class="h-8 w-full rounded-md border border-border bg-card pr-2 pl-8 text-xs"
          />
        </div>
        <label class="flex items-center gap-2 text-2xs text-muted-foreground">
          Expiring after
          <input type="date" [(ngModel)]="expiringAfter" class="numeric h-8 rounded-md border border-border bg-card px-2 text-xs text-foreground" />
        </label>
      </div>

      <mg-table-row-skeleton *ngIf="loading()" [columns]="8" [rows]="6" />

      <div *ngIf="!loading() && !error() && rows().length === 0" class="rounded-lg border border-border bg-card shadow-card">
        <mg-empty-state
          [icon]="Package"
          [title]="query() || filter() !== 'all' ? 'No batches match these filters' : 'No batches yet'"
          [description]="query() || filter() !== 'all' ? 'Try clearing the status filter or searching for a different batch number.' : 'Register your first medication batch and pair it with an IoT sensor to start monitoring.'"
        />
      </div>

      <mg-data-table *ngIf="!loading() && !error() && rows().length > 0" [rows]="rows()" [pageSize]="10" (rowClick)="goToBatch($event)">
        <ng-template mgColumn header="Batch Number" let-b>
          <span class="numeric text-xs font-bold text-foreground">{{ b.batchNumber }}</span>
        </ng-template>
        <ng-template mgColumn header="Drug Name" let-b>
          <span class="text-xs font-medium text-foreground">{{ b.drugName }}</span>
        </ng-template>
        <ng-template mgColumn header="Manufacturer" let-b>
          <span class="text-xs text-muted-foreground">{{ b.manufacturer }}</span>
        </ng-template>
        <ng-template mgColumn header="Status" let-b>
          <mg-status-badge [meta]="getBatchStatusMeta(b)" size="sm" />
        </ng-template>
        <ng-template mgColumn header="Safe Range" let-b>
          <span class="numeric text-xs text-foreground">{{ b.minTempC }}°C – {{ b.maxTempC }}°C</span>
        </ng-template>
        <ng-template mgColumn header="Quantity" align="right" let-b>
          <span class="numeric text-xs font-semibold text-foreground">
            {{ b.quantity | number }} <span class="font-normal text-muted-foreground">{{ b.unit }}</span>
          </span>
        </ng-template>
        <ng-template mgColumn header="Expiry" let-b>
          <span class="numeric text-xs font-semibold" [ngClass]="daysUntil(b.expiresAt) <= 30 ? 'text-warning' : 'text-foreground'">
            {{ formatDate(b.expiresAt) }}
            <span *ngIf="daysUntil(b.expiresAt) <= 30" class="ml-1 font-sans text-2xs">· {{ daysUntil(b.expiresAt) }}d</span>
          </span>
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
      </mg-data-table>

      <!-- New batch -->
      <mg-slide-over [open]="newOpen()" title="New batch" subtitle="Register a batch and pair it with a sensor" (close)="closeNew()">
        <form (ngSubmit)="submitNew()" class="space-y-5" novalidate>
          <div class="space-y-3">
            <h3 class="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Identification</h3>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="mb-1 block text-xs font-medium text-foreground">Batch number <span class="text-critical">*</span></label>
                <input [(ngModel)]="form.batchNumber" name="batchNumber" placeholder="BX-2026-0417-A" class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
              <div class="col-span-2">
                <label class="mb-1 block text-xs font-medium text-foreground">Drug name <span class="text-critical">*</span></label>
                <input [(ngModel)]="form.drugName" name="drugName" placeholder="Insulin Glargine 100 IU/mL" class="w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
              <div class="col-span-2">
                <label class="mb-1 block text-xs font-medium text-foreground">Manufacturer <span class="text-critical">*</span></label>
                <input [(ngModel)]="form.manufacturer" name="manufacturer" placeholder="Sanofi-Aventis" class="w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
            </div>
          </div>

          <div class="space-y-3">
            <h3 class="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Cold-chain envelope</h3>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="mb-1 block text-xs font-medium text-foreground">Min temp (°C) <span class="text-critical">*</span></label>
                <input type="number" step="0.1" [(ngModel)]="form.minTempC" name="minTempC" class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
              <div>
                <label class="mb-1 block text-xs font-medium text-foreground">Max temp (°C) <span class="text-critical">*</span></label>
                <input type="number" step="0.1" [(ngModel)]="form.maxTempC" name="maxTempC" class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
              <div class="col-span-2">
                <label class="mb-1 block text-xs font-medium text-foreground">Quantity (units) <span class="text-critical">*</span></label>
                <input type="number" [(ngModel)]="form.quantity" name="quantity" placeholder="4800" class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
            </div>
          </div>

          <div class="space-y-3">
            <h3 class="text-xs font-semibold tracking-wide text-muted-foreground uppercase">Dates</h3>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="mb-1 block text-xs font-medium text-foreground">Manufactured <span class="text-critical">*</span></label>
                <input type="date" [(ngModel)]="form.manufacturedAt" name="manufacturedAt" class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
              <div>
                <label class="mb-1 block text-xs font-medium text-foreground">Expires <span class="text-critical">*</span></label>
                <input type="date" [(ngModel)]="form.expiresAt" name="expiresAt" class="numeric w-full rounded-md border border-border bg-background px-3 py-2 text-xs" />
              </div>
            </div>
          </div>

          <p *ngIf="formError()" class="text-xs text-critical">{{ formError() }}</p>

          <div class="flex justify-end gap-2">
            <button type="button" (click)="closeNew()" class="rounded-md border border-border px-3 py-1.5 text-xs font-semibold text-foreground hover:bg-muted">
              Cancel
            </button>
            <button type="submit" [disabled]="creating()" class="rounded-md bg-primary-600 px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-60">
              {{ creating() ? 'Registering…' : 'Register batch' }}
            </button>
          </div>
        </form>
      </mg-slide-over>
    </div>
  `,
})
export class BatchesPageComponent implements OnInit {
  batches = signal<Batch[]>([]);
  loading = signal(true);
  error = signal(false);

  filter = signal<StatusFilter>('all');
  query = signal('');
  expiringAfter = signal('');

  newOpen = signal(false);
  creating = signal(false);
  formError = signal<string | null>(null);
  form: BatchForm = { ...EMPTY_FORM };

  filters = FILTERS;
  batchStatusMeta = batchStatusMeta;
  readingLevelMeta = readingLevelMeta;
  toneText = toneText;
  readingLevel = readingLevel;
  timeAgo = timeAgo;
  daysUntil = daysUntil;
  formatDate = formatDate;

  Package = LucidePackage;
  PackagePlus = LucidePackagePlus;
  Search = LucideSearch;

  rows = computed(() => {
    const filter = this.filter();
    const query = this.query().trim().toLowerCase();
    const from = this.expiringAfter();
    return this.batches().filter((b) => {
      if (filter !== 'all' && b.status !== filter) return false;
      if (from && new Date(b.expiresAt).getTime() < new Date(from).getTime()) return false;
      return !query || b.batchNumber.toLowerCase().includes(query) || b.drugName.toLowerCase().includes(query);
    });
  });

  constructor(private api: MedGuardApiService, private router: Router) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api.getBatches().subscribe({
      next: (batches) => {
        this.batches.set(batches);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  goToBatch(batch: Batch): void {
    this.router.navigate(['/batches', batch.id]);
  }

  getBatchStatusMeta(batch: Batch): StatusMeta {
    return batchStatusMeta[batch.status];
  }

  openNew(): void {
    this.form = { ...EMPTY_FORM };
    this.formError.set(null);
    this.newOpen.set(true);
  }

  closeNew(): void {
    this.newOpen.set(false);
  }

  submitNew(): void {
    const f = this.form;
    const batchNumber = f.batchNumber.trim();
    const drugName = f.drugName.trim();
    const manufacturer = f.manufacturer.trim();
    const minTempC = Number(f.minTempC);
    const maxTempC = Number(f.maxTempC);
    const quantity = Number(f.quantity);

    if (!/^[A-Za-z0-9-]{3,40}$/.test(batchNumber)) {
      this.formError.set('Batch number must be 3–40 characters: letters, digits and dashes only.');
      return;
    }
    if (drugName.length < 2) {
      this.formError.set('Drug name is required.');
      return;
    }
    if (manufacturer.length < 2) {
      this.formError.set('Manufacturer is required.');
      return;
    }
    if (!Number.isFinite(minTempC) || !Number.isFinite(maxTempC) || maxTempC <= minTempC) {
      this.formError.set('Max temp must be above min temp.');
      return;
    }
    if (!Number.isInteger(quantity) || quantity <= 0) {
      this.formError.set('Quantity must be a positive whole number.');
      return;
    }
    if (!f.manufacturedAt || !f.expiresAt) {
      this.formError.set('Both manufacture and expiry dates are required.');
      return;
    }
    if (new Date(f.expiresAt) <= new Date(f.manufacturedAt)) {
      this.formError.set('Expiry must be after the manufacture date.');
      return;
    }

    this.formError.set(null);
    this.creating.set(true);
    this.api
      .createBatch({
        batchNumber,
        drugName,
        manufacturer,
        minTempC,
        maxTempC,
        quantity,
        manufacturedAt: new Date(f.manufacturedAt).toISOString(),
        expiresAt: new Date(f.expiresAt).toISOString(),
      })
      .subscribe({
        next: (created) => {
          this.batches.update((list) => [created, ...list]);
          this.creating.set(false);
          this.closeNew();
        },
        error: () => {
          this.creating.set(false);
          this.formError.set('Could not register batch. The batch number may already be taken.');
        },
      });
  }
}
