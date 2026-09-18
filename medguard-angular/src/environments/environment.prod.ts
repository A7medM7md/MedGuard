export const environment = {
  production: true,
  useDummyData: false,
  apiBaseUrl: 'https://api.medguard.runasp.net/api/v1/',
  identity: {
    authority: 'https://identity.medguard.runasp.net',
    clientId: 'medguard-angular',
    scope: 'openid profile roles medguard.api offline_access',
    apiScope: 'medguard.api',
  },
};
