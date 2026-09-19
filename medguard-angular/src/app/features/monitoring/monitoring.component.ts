import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/**
 * PLACEHOLDER — next page to build. Following the same pattern as
 * DashboardPageComponent: inject the relevant *.service.ts (BatchService, AlertService, etc.), use signals for
 * loading/data state, reuse mg-data-table / mg-status-badge / mg-empty-state.
 */
@Component({
  selector: 'mg-monitoring-page',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent],
  templateUrl: './monitoring.component.html',
})
export class MonitoringPageComponent {}
