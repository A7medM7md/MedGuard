import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon, LucideTrendingDown, LucideTrendingUp } from '@lucide/angular';
import { StatusTone, toneAccent, toneText } from '../../../core/status';
import { IconRef } from '../../../core/icons';

export interface KpiTrend {
  direction: 'up' | 'down';
  label: string;
}

@Component({
  selector: 'mg-kpi-card',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  templateUrl: './kpi-card.component.html',
})
export class KpiCardComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: number | string;
  @Input({ required: true }) icon!: IconRef;
  @Input() tone: StatusTone | 'none' = 'none';
  @Input() trend?: KpiTrend;
  @Input() hint?: string;

  toneAccent = toneAccent;
  toneText = toneText;
  LucideTrendingUp = LucideTrendingUp;
  LucideTrendingDown = LucideTrendingDown;

  get isProblem(): boolean {
    return this.tone === 'critical' || this.tone === 'warning';
  }

  /** `tone !== 'none'` narrows `StatusTone | 'none'` to `StatusTone` in-place, so
   * indexing `toneText` here type-checks — unlike gating on the opaque
   * `isProblem` getter, which TS can't correlate back to `tone`'s type. */
  get iconToneClass(): string {
    return this.tone !== 'none' && this.isProblem ? toneText[this.tone] : 'text-muted-foreground';
  }

  get valueToneClass(): string {
    return this.tone !== 'none' && this.isProblem ? toneText[this.tone] : 'text-foreground';
  }
}
