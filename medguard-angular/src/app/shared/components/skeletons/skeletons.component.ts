import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'mg-kpi-card-skeleton',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="animate-pulse rounded-lg border border-l-4 border-border border-l-muted bg-card p-4 shadow-card">
      <div class="h-3 w-24 rounded bg-muted"></div>
      <div class="mt-3 h-7 w-16 rounded bg-muted"></div>
      <div class="mt-3 h-3 w-32 rounded bg-muted"></div>
    </div>
  `,
})
export class KpiCardSkeletonComponent {}

@Component({
  selector: 'mg-table-row-skeleton',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="divide-y divide-border">
      <div *ngFor="let r of rowsArray" class="grid animate-pulse gap-4 px-4 py-3" [style.gridTemplateColumns]="gridTemplate">
        <div *ngFor="let c of colsArray" class="h-3.5 rounded bg-muted"></div>
      </div>
    </div>
  `,
})
export class TableRowSkeletonComponent {
  @Input() rows = 3;
  @Input() columns = 4;

  get rowsArray() {
    return Array.from({ length: this.rows });
  }
  get colsArray() {
    return Array.from({ length: this.columns });
  }
  get gridTemplate() {
    return `repeat(${this.columns}, minmax(0, 1fr))`;
  }
}
