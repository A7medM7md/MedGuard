import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon } from '@lucide/angular';
import { StatusMeta, toneBg, toneBorder, toneText } from '../../../core/status';

@Component({
  selector: 'mg-status-badge',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  template: `
    <span
      class="inline-flex items-center gap-1.5 rounded-md border font-semibold whitespace-nowrap"
      [ngClass]="[toneBg[meta.tone], toneBorder[meta.tone], toneText[meta.tone], sizeClasses, pulse ? 'animate-status-pulse' : '']"
    >
      <svg [lucideIcon]="meta.icon" [class]="iconSize" aria-hidden="true"></svg>
      {{ meta.label }}
    </span>
  `,
})
export class StatusBadgeComponent {
  @Input({ required: true }) meta!: StatusMeta;
  @Input() size: 'sm' | 'md' | 'lg' = 'md';
  @Input() pulse = false;

  toneBg = toneBg;
  toneBorder = toneBorder;
  toneText = toneText;

  get sizeClasses(): string {
    return {
      sm: 'px-1.5 py-0.5 text-2xs',
      md: 'px-2 py-1 text-xs',
      lg: 'px-3 py-1.5 text-sm',
    }[this.size];
  }

  get iconSize(): string {
    return this.size === 'lg' ? 'h-4 w-4 shrink-0' : 'h-3.5 w-3.5 shrink-0';
  }
}
