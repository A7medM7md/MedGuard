import { Component, Input, TemplateRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideDynamicIcon } from '@lucide/angular';
import { IconRef } from '../../../core/icons';

@Component({
  selector: 'mg-empty-state',
  standalone: true,
  imports: [CommonModule, LucideDynamicIcon],
  templateUrl: './empty-state.component.html',
})
export class EmptyStateComponent {
  @Input({ required: true }) icon!: IconRef;
  @Input({ required: true }) title!: string;
  @Input() description?: string;
  @Input() action?: TemplateRef<unknown>;
}
