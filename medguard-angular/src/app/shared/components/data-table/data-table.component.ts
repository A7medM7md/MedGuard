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
  template: `
    <div class="overflow-hidden rounded-lg border border-border bg-card shadow-card">
      <div class="overflow-x-auto">
        <table class="w-full border-collapse text-left">
          <thead>
            <tr class="border-b border-border bg-muted/40">
              <th
                *ngFor="let col of columns"
                class="px-4 py-2.5 text-2xs font-semibold tracking-wide text-muted-foreground uppercase"
                [ngClass]="col.align === 'right' ? 'text-right' : 'text-left'"
              >
                {{ col.header }}
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-border">
            <tr
              *ngFor="let row of pagedRows"
              class="cursor-pointer hover:bg-muted/40"
              (click)="rowClick.emit(row)"
            >
              <td *ngFor="let col of columns" class="px-4 py-2.5" [ngClass]="col.align === 'right' ? 'text-right' : 'text-left'">
                <ng-container [ngTemplateOutlet]="col.template" [ngTemplateOutletContext]="{ $implicit: row }"></ng-container>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div *ngIf="totalPages > 1" class="flex items-center justify-between border-t border-border px-4 py-2.5">
        <p class="numeric text-2xs text-muted-foreground">Page {{ page }} of {{ totalPages }}</p>
        <div class="flex gap-1.5">
          <button
            type="button"
            class="rounded-md border border-border px-2 py-1 text-2xs font-semibold disabled:opacity-40"
            [disabled]="page === 1"
            (click)="page = page - 1"
          >
            Prev
          </button>
          <button
            type="button"
            class="rounded-md border border-border px-2 py-1 text-2xs font-semibold disabled:opacity-40"
            [disabled]="page === totalPages"
            (click)="page = page + 1"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  `,
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
