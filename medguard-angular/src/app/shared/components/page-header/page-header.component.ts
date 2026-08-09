import { Component, Input, TemplateRef } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'mg-page-header',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3">
      <div class="min-w-0">
        <h1 class="text-xl font-bold tracking-tight text-foreground">{{ title }}</h1>
        <p *ngIf="description" class="mt-0.5 text-xs text-muted-foreground">{{ description }}</p>
      </div>
      <ng-container *ngIf="action" [ngTemplateOutlet]="action"></ng-container>
    </div>
  `,
})
export class PageHeaderComponent {
  @Input({ required: true }) title!: string;
  @Input() description?: string;
  @Input() action?: TemplateRef<unknown>;
}
