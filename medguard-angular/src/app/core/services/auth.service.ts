import { Injectable, signal } from '@angular/core';
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
  // Strict everywhere except the self-hosted build served over plain HTTP
  // (environment.selfhost.ts), where the issuer is the page's own origin.
  requireHttps: environment.identity.requireHttps,
};

/**
 * Where startup is, for the splash screen (AppComponent):
 *   starting    loading IdentityServer's discovery document
 *   signing-in  back from the login page, exchanging the code for tokens
 *   redirecting leaving for the login page
 *   ready       done; the router takes over
 *   error       IdentityServer unreachable or the sign-in failed
 */
export type AuthStatus = 'starting' | 'signing-in' | 'redirecting' | 'ready' | 'error';

/**
 * Thin wrapper around angular-oauth2-oidc's OAuthService. Every other part of
 * the app (guard, interceptor, app-shell user menu) goes through this, not
 * OAuthService directly, so the OIDC library is a one-file swap if it's ever
 * replaced.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly status = signal<AuthStatus>('starting');

  constructor(private oauthService: OAuthService) {
    this.oauthService.configure(authConfig);
  }

  /**
   * Called once at app startup (see app.config.ts) before routing runs. Never
   * throws: an unreachable IdentityServer used to fail the app initializer and
   * leave a blank page; now it's an 'error' status the splash shows with a retry.
   */
  async init(): Promise<void> {
    if (new URLSearchParams(window.location.search).has('code')) this.status.set('signing-in');
    try {
      this.oauthService.setupAutomaticSilentRefresh();
      await this.oauthService.loadDiscoveryDocumentAndTryLogin();
      this.status.set('ready');
    } catch (error) {
      console.error('Sign-in failed', error);
      this.status.set('error');
    }
  }

  login(): void {
    if (this.status() === 'error') return; // no discovery document: nowhere to redirect to
    this.status.set('redirecting');
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
