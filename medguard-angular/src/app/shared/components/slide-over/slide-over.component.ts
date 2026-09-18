import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon, LucideX } from '@lucide/angular';

@Component({
  selector: 'mg-slide-over',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  template: `
    <div *ngIf="open" class="fixed inset-0 z-50 flex justify-end bg-black/30">
      <button type="button" class="flex-1 cursor-default" aria-label="Close panel" (click)="close.emit()"></button>
      <aside
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="title"
        class="flex h-full w-full max-w-lg flex-col overflow-y-auto border-l border-border bg-card shadow-overlay"
      >
        <header class="sticky top-0 grid grid-cols-[minmax(0,1fr)_auto] items-start gap-3 border-b border-border bg-card px-4 py-3">
          <div class="min-w-0">
            <h2 class="numeric truncate text-base font-bold text-foreground">{{ title }}</h2>
            <p *ngIf="subtitle" class="truncate text-xs text-muted-foreground">{{ subtitle }}</p>
          </div>
          <button
            type="button"
            (click)="close.emit()"
            aria-label="Close panel"
            class="inline-flex h-8 w-8 items-center justify-center rounded-md border border-border text-muted-foreground hover:text-foreground"
          >
            <svg [lucideIcon]="X" class="h-4 w-4" aria-hidden="true"></svg>
          </button>
        </header>
        <div class="flex-1 space-y-5 p-4">
          <ng-content></ng-content>
        </div>
      </aside>
    </div>
  `,
})
export class SlideOverComponent {
  @Input() open = false;
  @Input() title = '';
  @Input() subtitle?: string;
  @Output() close = new EventEmitter<void>();

  X = LucideX;
}
