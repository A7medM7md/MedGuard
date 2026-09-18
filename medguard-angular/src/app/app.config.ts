import { ApplicationConfig, inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import { routes } from './app.routes';
import { AuthService } from './core/services/auth.service';
import { authInterceptor } from './core/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    // Angular 22 default: no zone.js — change detection runs off signals instead.
    // Every piece of state in this app (Dashboard, AppShell) is already a signal,
    // so this works with no extra wiring.
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideOAuthClient(),
    // Resolves the OIDC discovery document and completes any in-progress login
    // redirect BEFORE the router activates the first route — otherwise authGuard
    // would see a logged-out state on a hard refresh mid-login.
    provideAppInitializer(() => inject(AuthService).init()),
  ],
};
