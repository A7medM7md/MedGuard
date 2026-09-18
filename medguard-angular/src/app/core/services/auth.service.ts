import { Injectable } from '@angular/core';
import { AuthConfig, OAuthService } from 'angular-oauth2-oidc';
import { environment } from '../../../environments/environment';

const authConfig: AuthConfig = {
  issuer: environment.identity.authority,
  redirectUri: window.location.origin + '/',
  postLogoutRedirectUri: window.location.origin + '/',
  clientId: environment.identity.clientId,
  responseType: 'code',
  scope: environment.identity.scope,
  showDebugInformation: !environment.production,
  // The Duende dev cert on https://localhost:5001 is a real HTTPS issuer even in
  // local dev, so this stays strict — it's only ever relaxed for genuinely
  // non-HTTPS local backends, which this isn't.
  requireHttps: true,
};

/**
 * Thin wrapper around angular-oauth2-oidc's OAuthService. Every other part of
 * the app (guard, interceptor, app-shell user menu) goes through this, not
 * OAuthService directly, so the OIDC library is a one-file swap if it's ever
 * replaced.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  constructor(private oauthService: OAuthService) {
    this.oauthService.configure(authConfig);
  }

  /** Called once at app startup (see app.config.ts) before routing runs. */
  async init(): Promise<void> {
    this.oauthService.setupAutomaticSilentRefresh();
    await this.oauthService.loadDiscoveryDocumentAndTryLogin();
  }

  login(): void {
    this.oauthService.initCodeFlow();
  }

  logout(): void {
    this.oauthService.logOut();
  }

  get isAuthenticated(): boolean {
    return this.oauthService.hasValidAccessToken();
  }

  get accessToken(): string | null {
    return this.oauthService.getAccessToken();
  }

  private get claims(): Record<string, unknown> | null {
    return this.oauthService.getIdentityClaims() as Record<string, unknown> | null;
  }

  get displayName(): string {
    const claims = this.claims;
    return (claims?.['name'] as string) ?? (claims?.['email'] as string) ?? 'Signed in';
  }

  get role(): string | null {
    return (this.claims?.['role'] as string) ?? null;
  }
}
