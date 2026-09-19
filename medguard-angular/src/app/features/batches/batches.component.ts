import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon, LucidePackage, LucidePackagePlus, LucideSearch } from '@lucide/angular';

import { BatchService } from '../../core/services/batch.service';
import { Batch, BatchStatus } from '../../core/models';
import { batchStatusMeta, readingLevelMeta, StatusMeta, toneText } from '../../core/status';
import { daysUntil, formatDate, readingLevel, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { DataTableComponent, ColumnDirective } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { TableRowSkeletonComponent } from '../../shared/components/skeletons/table-row-skeleton.component';
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
  templateUrl: './batches.component.html',
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

  constructor(private batchService: BatchService, private router: Router) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.batchService.getBatches().subscribe({
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
    this.batchService
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
