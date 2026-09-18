export const environment = {
  production: false,
  /**
   * Flip to false once the MedGuard .NET API (see the backend solution —
   * MedGuard.API, Controllers: BatchesController, SensorReadingsController,
   * AlertsController, ShipmentsController) is deployed and reachable.
   */
  useDummyData: false,
  apiBaseUrl: 'https://localhost:7590/api',
};
