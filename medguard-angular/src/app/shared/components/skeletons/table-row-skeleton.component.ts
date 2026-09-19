import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'mg-table-row-skeleton',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './table-row-skeleton.component.html',
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
