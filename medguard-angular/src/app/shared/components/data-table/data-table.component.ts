import { Component, Input, Output, EventEmitter, TemplateRef, ContentChildren, QueryList, AfterContentInit, Directive } from '@angular/core';
import { CommonModule } from '@angular/common';

/**
 * Usage:
 * <mg-data-table [rows]="batches" [pageSize]="6" (rowClick)="onRowClick($event)">
 *   <ng-template mgColumn header="Batch #" let-row>{{ row.batchNumber }}</ng-template>
 *   <ng-template mgColumn header="Drug" let-row>{{ row.drugName }}</ng-template>
 * </mg-data-table>
 *
 * Kept intentionally simple (no built-in sort-by-header-click UI yet) —
 * add sorting when a page actually needs it rather than over-building now.
 */
@Directive({ selector: 'ng-template[mgColumn]', standalone: true })
export class ColumnDirective {
  @Input() header = '';
  @Input() align: 'left' | 'right' = 'left';
  constructor(public template: TemplateRef<unknown>) {}
}

@Component({
  selector: 'mg-data-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './data-table.component.html',
})
export class DataTableComponent<T> implements AfterContentInit {
  @Input({ required: true }) rows: T[] = [];
  @Input() pageSize = 10;
  @Output() rowClick = new EventEmitter<T>();

  @ContentChildren(ColumnDirective) columnDefs!: QueryList<ColumnDirective>;
  columns: ColumnDirective[] = [];
  page = 1;

  ngAfterContentInit(): void {
    this.columns = this.columnDefs.toArray();
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.rows.length / this.pageSize));
  }

  get pagedRows(): T[] {
    const start = (this.page - 1) * this.pageSize;
    return this.rows.slice(start, start + this.pageSize);
  }
}
