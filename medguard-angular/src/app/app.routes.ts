import { Routes } from '@angular/router';
import { AppShellComponent } from './shared/components/app-shell/app-shell.component';

export const routes: Routes = [
  {
    path: '',
    component: AppShellComponent,
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardPageComponent),
      },
      {
        path: 'batches',
        loadComponent: () =>
          import('./features/batches/batches.component').then((m) => m.BatchesPageComponent),
      },
      {
        path: 'batches/:batchId',
        loadComponent: () =>
          import('./features/batch-detail/batch-detail.component').then((m) => m.BatchDetailPageComponent),
      },
      {
        path: 'monitoring',
        loadComponent: () =>
          import('./features/monitoring/monitoring.component').then((m) => m.MonitoringPageComponent),
      },
      {
        path: 'alerts',
        loadComponent: () =>
          import('./features/alerts/alerts.component').then((m) => m.AlertsPageComponent),
      },
      {
        path: 'shipments',
        loadComponent: () =>
          import('./features/shipments/shipments.component').then((m) => m.ShipmentsPageComponent),
      },
      {
        path: 'devices',
        loadComponent: () =>
          import('./features/devices/devices.component').then((m) => m.DevicesPageComponent),
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings.component').then((m) => m.SettingsPageComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
