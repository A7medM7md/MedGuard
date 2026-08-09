import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/**
 * PLACEHOLDER — next page to build. Following the same pattern as
 * DashboardPageComponent: inject MedGuardApiService, use signals for
 * loading/data state, reuse mg-data-table / mg-status-badge / mg-empty-state.
 */
@Component({
  selector: 'mg-settings-page',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent],
  template: `
    <div class="space-y-5">
      <mg-page-header title="Settings" description="Coming next — built the same way as the Dashboard page." />
      <div class="rounded-lg border border-dashed border-border bg-card p-8 text-center shadow-card">
        <p class="text-sm text-muted-foreground">This page is next in line to build.</p>
      </div>
    </div>
  `,
})
export class SettingsPageComponent {}
