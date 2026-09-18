export const environment = {
  production: false,
  /**
   * Flip to false once the MedGuard .NET API (see the backend solution —
   * MedGuard.API, Controllers: BatchesController, SensorReadingsController,
   * AlertsController, ShipmentsController) is deployed and reachable.
   */
  useDummyData: false,
  // apiBaseUrl: 'https://localhost:7590/api',
  apiBaseUrl: 'http://localhost:7591/api',
  identity: {
    authority: 'https://localhost:5001',
    clientId: 'medguard-angular',
    scope: 'openid profile roles medguard.api offline_access',
    apiScope: 'medguard.api',
  },
};
