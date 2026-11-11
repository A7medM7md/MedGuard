import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LucideDynamicIcon, LucideActivity, LucidePackage } from '@lucide/angular';
import { Subscription, forkJoin, interval, of, startWith, switchMap } from 'rxjs';
import { catchError, filter, map } from 'rxjs/operators';

import { BatchService } from '../../core/services/batch.service';
import { LiveAlert, LiveFeedService, mapServerBatchStatus } from '../../core/services/live-feed.service';
import { Batch, Reading } from '../../core/models';
import { batchStatusMeta, readingLevelMeta, StatusMeta, toneBorder, toneText } from '../../core/status';
import { readingLevel, ReadingLevel, timeAgo } from '../../core/format';

import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { SparklineComponent } from '../../shared/components/sparkline/sparkline.component';

/** Fallback only: used while the live feed isn't connected (always, in production). */
const POLL_MS = 10_000;
/** Same window the batch detail endpoint returns, so the sparkline keeps its shape. */
const MAX_READINGS = 50;
const MAX_RECENT_ALERTS = 5;
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
  /** Alerts pushed since the page opened, newest first. */
  recentAlerts = signal<LiveAlert[]>([]);

  // inject(), not a constructor parameter: with target ES2022, class fields are
  // initialized BEFORE constructor parameter properties are assigned, so
  // `feedState = this.liveFeed.state` would read undefined.
  private liveFeed = inject(LiveFeedService);
  feedState = this.liveFeed.state;

  batchStatusMeta = batchStatusMeta;
  readingLevelMeta = readingLevelMeta;
  toneText = toneText;
  toneBorder = toneBorder;
  timeAgo = timeAgo;

  Activity = LucideActivity;
  Package = LucidePackage;

  private subs = new Subscription();

  constructor(private batchService: BatchService) {}

  ngOnInit(): void {
    void this.liveFeed.connect();

    // Push first, poll as a fallback (ADR 0007). The first tick always loads
    // the snapshot (pushes only carry CHANGES). After that, a tick only fetches
    // while the feed isn't live: production, Kafka down, or mid-reconnect.
    this.subs.add(
      interval(POLL_MS)
        .pipe(
          startWith(-1),
          filter((tick) => tick === -1 || this.liveFeed.state() !== 'live'),
          switchMap(() => this.fetchLive()),
        )
        .subscribe(),
    );

    this.subs.add(this.liveFeed.readings$.subscribe((readings) => this.applyReadings(readings)));
    this.subs.add(this.liveFeed.alerts$.subscribe((alert) => this.applyAlert(alert)));
    // Anything pushed while the connection was down is gone: resync once.
    this.subs.add(this.liveFeed.reconnected$.pipe(switchMap(() => this.fetchLive())).subscribe());
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
    void this.liveFeed.disconnect();
  }

  severityTone(alert: LiveAlert): string {
    return toneText[alert.severity === 'Critical' ? 'critical' : 'warning'];
  }

  /** Conflated by the server: at most one (the newest) reading per batch per push. */
  private applyReadings(readings: Reading[]): void {
    const byBatch = new Map(readings.map((r) => [r.batchId, r]));
    let changed = false;

    const cards = this.cards().map((card) => {
      const reading = byBatch.get(card.batch.id);
      // Unknown batch (not monitored, or doesn't exist) or already applied: ignore.
      if (!reading || card.readings.some((r) => r.id === reading.id)) return card;
      changed = true;

      const next = [...card.readings, reading]
        .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime())
        .slice(-MAX_READINGS);
      const lastReading = next[next.length - 1];
      return { ...card, readings: next, lastReading, level: readingLevel(card.batch, lastReading) };
    });

    if (changed) this.cards.set(sortByLevel(cards));
  }

  private applyAlert(alert: LiveAlert): void {
    this.recentAlerts.update((list) => [alert, ...list].slice(0, MAX_RECENT_ALERTS));

    const status = mapServerBatchStatus(alert.batchStatus);
    const known = this.cards().some((c) => c.batch.id === alert.batchId);
    if (!known) {
      // A batch we aren't showing yet (created after the snapshot). Rare, so a
      // full refetch is fine here.
      if (status && MONITORED_STATUSES.includes(status)) this.fetchLive().subscribe();
      return;
    }

    // The status change (e.g. auto-quarantine) arrives with the alert, so the
    // badge flips without waiting for anything else.
    if (status) {
      this.cards.set(
        sortByLevel(
          this.cards().map((c) => (c.batch.id === alert.batchId ? { ...c, batch: { ...c.batch, status } } : c)),
        ),
      );
    }
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

        return forkJoin(detailCalls).pipe(map(sortByLevel));
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

function sortByLevel(cards: MonitorCard[]): MonitorCard[] {
  // A quarantined batch stays a problem batch after its temperature recovers: it is
  // held until someone reviews it, so it ranks right after live critical readings.
  const rank = (c: MonitorCard) =>
    c.level === 'critical' ? 0 : c.batch.status === 'quarantined' ? 1 : LEVEL_ORDER[c.level] + 1;
  return [...cards].sort((a, b) => rank(a) - rank(b));
}
