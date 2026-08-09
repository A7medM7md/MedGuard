import { Component, Input, TemplateRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon } from '@lucide/angular';
import { IconRef } from '../../../core/icons';

@Component({
  selector: 'mg-empty-state',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  template: `
    <div class="flex flex-col items-center justify-center gap-3 px-6 py-12 text-center">
      <div class="flex h-12 w-12 items-center justify-center rounded-full bg-muted">
        <svg [lucideIcon]="icon" class="h-6 w-6 text-muted-foreground" aria-hidden="true"></svg>
      </div>
      <div class="max-w-sm">
        <p class="text-sm font-semibold text-foreground">{{ title }}</p>
        <p *ngIf="description" class="mt-1 text-xs text-muted-foreground">{{ description }}</p>
      </div>
      <ng-container *ngIf="action" [ngTemplateOutlet]="action"></ng-container>
    </div>
  `,
})
export class EmptyStateComponent {
  @Input({ required: true }) icon!: IconRef;
  @Input({ required: true }) title!: string;
  @Input() description?: string;
  @Input() action?: TemplateRef<unknown>;
}
