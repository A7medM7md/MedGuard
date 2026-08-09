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
  template: `
    <div
      class="rounded-lg border border-l-4 border-border bg-card p-4 shadow-card"
      [ngClass]="tone !== 'none' ? toneAccent[tone] : 'border-l-primary-600'"
    >
      <div class="flex items-start justify-between gap-3">
        <p class="text-xs font-medium tracking-wide text-muted-foreground uppercase">{{ label }}</p>
        <svg
          [lucideIcon]="icon"
          class="h-4 w-4 shrink-0"
          [ngClass]="iconToneClass"
          aria-hidden="true"
        ></svg>
      </div>
      <p class="display-figure mt-3 text-2xl" [ngClass]="valueToneClass">
        {{ value }}
      </p>
      <div class="mt-2 flex items-center gap-1.5 text-2xs text-muted-foreground">
        <ng-container *ngIf="trend; else hintTpl">
          <svg [lucideIcon]="trend.direction === 'up' ? LucideTrendingUp : LucideTrendingDown" class="h-3.5 w-3.5" aria-hidden="true"></svg>
          <span>{{ trend.label }}</span>
        </ng-container>
        <ng-template #hintTpl>
          <span>{{ hint }}</span>
        </ng-template>
      </div>
    </div>
  `,
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
