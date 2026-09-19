import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon, LucideX } from '@lucide/angular';

@Component({
  selector: 'mg-slide-over',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  templateUrl: './slide-over.component.html',
})
export class SlideOverComponent {
  @Input() open = false;
  @Input() title = '';
  @Input() subtitle?: string;
  @Output() close = new EventEmitter<void>();

  X = LucideX;
}
