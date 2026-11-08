export const environment = {
  production: false,
  /**
   * Flip to false once the MedGuard .NET API (see the backend solution —
   * MedGuard.API, Controllers: BatchesController, SensorReadingsController,
   * AlertsController, ShipmentsController) is deployed and reachable.
   */
  useDummyData: false,
  // apiBaseUrl: 'https://localhost:7590/api',
  apiBaseUrl: 'https://medguard.runasp.net/api',
  /**
   * SignalR hub for the live Monitoring feed (ADR 0007). Only exists on a local
   * API started with the dev launch profile (Notifier__Enabled=true) and Kafka
   * running. Against any other API the connect fails and the page polls.
   */
  // monitoringHubUrl: 'https://localhost:7590/hubs/monitoring' as string | null,
  monitoringHubUrl: null as string | null,
  identity: {
    // authority: 'https://localhost:5001',
    authority: 'https://medguard-identity.runasp.net',
    clientId: 'medguard-angular',
    scope: 'openid profile roles medguard.api offline_access',
    apiScope: 'medguard.api',
  },
};
