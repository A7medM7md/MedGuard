export const environment = {
  production: true,
  useDummyData: false,
  apiBaseUrl: 'https://medguard.runasp.net/api',
  identity: {
    authority: 'https://medguard-identity.runasp.net',
    clientId: 'medguard-angular',
    scope: 'openid profile roles medguard.api offline_access',
    apiScope: 'medguard.api',
  },
};
