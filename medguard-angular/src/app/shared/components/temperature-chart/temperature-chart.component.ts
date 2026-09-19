import { Component, Input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon, LucideThermometer } from '@lucide/angular';
import { Reading } from '../../../core/models';
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
  templateUrl: './temperature-chart.component.html',
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
