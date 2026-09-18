import { Component, Input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon, LucideThermometer } from '@lucide/angular';
import { Reading } from '../../../core/models/medguard.models';
import { formatClock, formatDateTime } from '../../../core/format';

type RangeKey = '1h' | '24h' | '7d' | 'all';

const RANGES: { key: RangeKey; label: string; hours: number }[] = [
  { key: '1h', label: 'Last hour', hours: 1 },
  { key: '24h', label: '24h', hours: 24 },
  { key: '7d', label: '7d', hours: 168 },
  { key: 'all', label: 'All', hours: Number.POSITIVE_INFINITY },
];

const VIEW_W = 600;
const VIEW_H = 240;
const PAD = 8;

/**
 * Lightweight pure-SVG line chart — no charting library dependency. Plots
 * temperature readings against the batch's safe range, with a shaded band
 * for the safe zone and a time-range filter. Simpler than the original
 * design's recharts version (no hover tooltip, no humidity overlay) —
 * a deliberate scope trim, not an oversight.
 */
@Component({
  selector: 'mg-temperature-chart',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  template: `
    <section class="rounded-lg border border-border bg-card p-4 shadow-card">
      <header class="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div class="min-w-0">
          <h2 class="flex items-center gap-2 text-base font-semibold text-foreground">
            <svg [lucideIcon]="Thermometer" class="h-4 w-4 shrink-0 text-critical" aria-hidden="true"></svg>
            Temperature history
          </h2>
          <p class="mt-0.5 text-xs text-muted-foreground">
            Shaded band is the approved safe range
            <span class="numeric font-semibold text-foreground">{{ minTempC }}–{{ maxTempC }} °C</span>
          </p>
        </div>
        <div class="flex rounded-md border border-border p-0.5" role="group" aria-label="Time range">
          <button
            *ngFor="let r of ranges"
            type="button"
            (click)="range.set(r.key)"
            [attr.aria-pressed]="range() === r.key"
            class="rounded-sm px-2 py-1 text-2xs font-semibold"
            [ngClass]="range() === r.key ? 'bg-primary-600 text-white' : 'text-muted-foreground hover:text-foreground'"
          >
            {{ r.label }}
          </button>
        </div>
      </header>

      <div *ngIf="filteredReadings.length < 2" class="flex items-center justify-center text-xs text-muted-foreground" [style.height.px]="height">
        Not enough readings yet to draw a chart.
      </div>

      <svg
        *ngIf="filteredReadings.length >= 2"
        [attr.viewBox]="'0 0 ' + viewW + ' ' + viewH"
        preserveAspectRatio="none"
        [style.height.px]="height"
        class="w-full"
      >
        <rect [attr.x]="PAD" [attr.y]="bandY2" [attr.width]="viewW - PAD * 2" [attr.height]="bandY1 - bandY2" fill="var(--safe)" fill-opacity="0.08" />
        <line [attr.x1]="PAD" [attr.x2]="viewW - PAD" [attr.y1]="bandY1" [attr.y2]="bandY1" stroke="var(--safe)" stroke-dasharray="4 4" stroke-opacity="0.7" />
        <line [attr.x1]="PAD" [attr.x2]="viewW - PAD" [attr.y1]="bandY2" [attr.y2]="bandY2" stroke="var(--safe)" stroke-dasharray="4 4" stroke-opacity="0.7" />
        <path [attr.d]="linePath" fill="none" stroke="var(--critical)" stroke-width="2" vector-effect="non-scaling-stroke" />
      </svg>

      <div *ngIf="filteredReadings.length >= 2" class="mt-1 flex justify-between text-2xs text-muted-foreground">
        <span class="numeric" [title]="fullTimestamp(filteredReadings[0])">{{ clock(filteredReadings[0]) }}</span>
        <span class="numeric" [title]="fullTimestamp(filteredReadings[filteredReadings.length - 1])">
          {{ clock(filteredReadings[filteredReadings.length - 1]) }}
        </span>
      </div>
    </section>
  `,
})
export class TemperatureChartComponent {
  @Input({ required: true }) readings: Reading[] = [];
  @Input({ required: true }) minTempC = 0;
  @Input({ required: true }) maxTempC = 0;
  @Input() height = 240;

  range = signal<RangeKey>('24h');
  ranges = RANGES;
  Thermometer = LucideThermometer;

  viewW = VIEW_W;
  viewH = VIEW_H;
  PAD = PAD;

  clock = (r: Reading) => formatClock(r.recordedAt);
  fullTimestamp = (r: Reading) => formatDateTime(r.recordedAt);

  get filteredReadings(): Reading[] {
    const sorted = [...this.readings].sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime());
    const hours = RANGES.find((r) => r.key === this.range())!.hours;
    if (!Number.isFinite(hours) || sorted.length === 0) return sorted;
    const last = new Date(sorted[sorted.length - 1].recordedAt).getTime();
    return sorted.filter((r) => new Date(r.recordedAt).getTime() >= last - hours * 3_600_000);
  }

  private get scale(): { min: number; max: number } {
    const temps = this.filteredReadings.map((r) => r.temperatureC);
    const min = Math.min(this.minTempC, ...temps) - 2;
    const max = Math.max(this.maxTempC, ...temps) + 2;
    return { min, max };
  }

  private yFor(temp: number): number {
    const { min, max } = this.scale;
    const span = max - min || 1;
    return VIEW_H - PAD - ((temp - min) / span) * (VIEW_H - PAD * 2);
  }

  get bandY1(): number {
    return this.yFor(this.minTempC);
  }

  get bandY2(): number {
    return this.yFor(this.maxTempC);
  }

  get linePath(): string {
    const points = this.filteredReadings;
    if (points.length < 2) return '';
    return points
      .map((r, i) => {
        const x = PAD + (i / (points.length - 1)) * (VIEW_W - PAD * 2);
        const y = this.yFor(r.temperatureC);
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`;
      })
      .join(' ');
  }
}
