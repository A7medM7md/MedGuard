export const environment = {
  production: true,
  useDummyData: false,
  apiBaseUrl: 'https://medguard.runasp.net/api',
  // Production has no Kafka (ADR 0001), so no live feed: Monitoring polls.
  monitoringHubUrl: null as string | null,
  identity: {
    authority: 'https://medguard-identity.runasp.net',
    clientId: 'medguard-angular',
    scope: 'openid profile roles medguard.api offline_access',
    apiScope: 'medguard.api',
  },
};
