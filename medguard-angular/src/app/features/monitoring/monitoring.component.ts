import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LucideDynamicIcon, LucideActivity, LucidePackage } from '@lucide/angular';
import { Subscription, forkJoin, interval, of, startWith, switchMap } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { BatchService } from '../../core/services/batch.service';
import { Batch, Reading } from '../../core/models';
import { batchStatusMeta, readingLevelMeta, StatusMeta, toneBorder, toneText } from '../../core/status';
import { readingLevel, ReadingLevel, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { SparklineComponent } from '../../shared/components/sparkline/sparkline.component';

const POLL_MS = 10_000;
const LEVEL_ORDER: Record<ReadingLevel, number> = { critical: 0, warning: 1, safe: 2 };
const MONITORED_STATUSES: Batch['status'][] = ['active', 'in_transit', 'quarantined'];

interface MonitorCard {
  batch: Batch;
  readings: Reading[];
  lastReading: Reading | null;
  level: ReadingLevel;
}

@Component({
  selector: 'mg-monitoring-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    LucideDynamicIcon,
    PageHeaderComponent,
    StatusBadgeComponent,
    EmptyStateComponent,
    SparklineComponent,
  ],
  templateUrl: './monitoring.component.html',
})
export class MonitoringPageComponent implements OnInit, OnDestroy {
  cards = signal<MonitorCard[]>([]);
  loading = signal(true);
  error = signal(false);

  batchStatusMeta = batchStatusMeta;
  readingLevelMeta = readingLevelMeta;
  toneText = toneText;
  toneBorder = toneBorder;
  timeAgo = timeAgo;

  Activity = LucideActivity;
  Package = LucidePackage;

  private pollSub?: Subscription;

  constructor(private batchService: BatchService) {}

  ngOnInit(): void {
    this.pollSub = interval(POLL_MS)
      .pipe(startWith(0), switchMap(() => this.fetchLive()))
      .subscribe();
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.fetchLive().subscribe();
  }

  getBatchStatusMeta(batch: Batch): StatusMeta {
    return batchStatusMeta[batch.status];
  }

  private fetchLive() {
    return this.batchService.getBatches().pipe(
      switchMap((batches) => {
        const monitored = batches.filter((b) => MONITORED_STATUSES.includes(b.status));
        if (monitored.length === 0) return of([] as MonitorCard[]);

        const detailCalls = monitored.map((batch) =>
          this.batchService.getBatchDetail(batch.id).pipe(
            map((detail): MonitorCard => {
              const readings = [...detail.readings].sort(
                (a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime(),
              );
              const lastReading = readings.length ? readings[readings.length - 1] : null;
              return { batch: detail.batch, readings, lastReading, level: readingLevel(detail.batch, lastReading) };
            }),
          ),
        );

        return forkJoin(detailCalls).pipe(
          map((cards) => [...cards].sort((a, b) => LEVEL_ORDER[a.level] - LEVEL_ORDER[b.level])),
        );
      }),
      map((cards) => {
        this.cards.set(cards);
        this.loading.set(false);
        this.error.set(false);
        return cards;
      }),
      catchError(() => {
        this.error.set(true);
        this.loading.set(false);
        return of([] as MonitorCard[]);
      }),
    );
  }
}
