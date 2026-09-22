import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Reading } from '../../../core/models';
import { StatusTone, toneVar } from '../../../core/status';

/** Compact one-line trend indicator used on the Live Monitoring cards. */
@Component({
  selector: 'mg-sparkline',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './sparkline.component.html',
})
export class SparklineComponent {
  @Input({ required: true }) readings: Reading[] = [];
  @Input() tone: StatusTone = 'neutral';

  toneVar = toneVar;

  get strokeColor(): string {
    return toneVar[this.tone];
  }

  get path(): string {
    const points = this.readings.slice(-36);
    if (points.length < 2) return '';

    const temps = points.map((p) => p.temperatureC);
    const min = Math.min(...temps);
    const max = Math.max(...temps);
    const span = max - min || 1;

    return points
      .map((p, i) => {
        const x = (i / (points.length - 1)) * 100;
        const y = 28 - ((p.temperatureC - min) / span) * 24 - 2;
        return `${i === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`;
      })
      .join(' ');
  }
}
