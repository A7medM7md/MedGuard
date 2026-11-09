/**
 * Self-hosted build (`ng build --configuration selfhost`), used by the Docker
 * image: the SPA is served by the API's Kestrel from wwwroot, and IdentityServer
 * sits on the same origin under /identity (see deploy/nginx/edge.conf).
 *
 * Every URL is derived from the page's own origin at runtime, so one build
 * works on http:// and https://, and on any host or port.
 */
const origin = window.location.origin;

export const environment = {
  production: true,
  useDummyData: false,
  apiBaseUrl: `${origin}/api`,
  // The Docker stack runs Kafka, so the API hosts the live feed (ADR 0007).
  monitoringHubUrl: `${origin}/hubs/monitoring` as string | null,
  identity: {
    authority: `${origin}/identity`,
    clientId: 'medguard-angular',
    scope: 'openid profile roles medguard.api offline_access',
    apiScope: 'medguard.api',
    // IdentityServer shares the page's origin, so it is HTTPS exactly when the
    // page is. Only plain HTTP while no TLS certificate is installed yet.
    requireHttps: window.location.protocol === 'https:',
  },
};
