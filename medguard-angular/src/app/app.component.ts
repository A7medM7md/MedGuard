import { Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, take } from 'rxjs';
import { AuthService } from './core/services/auth.service';

/**
 * Root. Until the first page has rendered it shows the startup splash: the same
 * markup and classes as the static one in index.html (styled there), which this
 * replaces seamlessly when Angular boots. It covers the discovery-document fetch,
 * the token exchange after login, and the redirect TO the login page — a
 * cancelled first navigation (authGuard -> login()) keeps it up while the
 * browser leaves, instead of showing an empty page.
 */
@Component({
  selector: 'mg-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `
    @if (!navigated()) {
      <div class="mg-splash" role="status" aria-live="polite">
        <img class="mg-splash-logo" src="medguard-logo.svg" alt="" />
        <div>
          <p class="mg-splash-title">MedGuard</p>
          <p class="mg-splash-sub">COLD-CHAIN CONTROL</p>
        </div>
        @if (auth.status() === 'error') {
          <p class="mg-splash-status mg-splash-error">Couldn't reach the sign-in server.</p>
          <button type="button" class="mg-splash-button" (click)="retry()">Try again</button>
        } @else {
          <div class="mg-splash-bar" aria-hidden="true"></div>
          <p class="mg-splash-status">{{ message() }}</p>
          <p class="mg-splash-hint" [style.animation-delay.ms]="hintDelayMs">
            Connecting to the secure sign-in server. The demo runs on free hosting, so the first visit can take a few seconds.
          </p>
        }
      </div>
    }
    <router-outlet />
  `,
})
export class AppComponent {
  protected auth = inject(AuthService);
  private router = inject(Router);

  /** True once the first page has actually rendered. */
  protected navigated = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      take(1),
      map(() => true),
    ),
    { initialValue: false },
  );

  protected message = computed(() => {
    switch (this.auth.status()) {
      case 'signing-in':
        return 'Signing you in…';
      case 'redirecting':
        return 'Redirecting to secure sign-in…';
      case 'ready':
        return 'Loading your dashboard…';
      default:
        return 'Starting MedGuard…';
    }
  });

  /** The hint fades in 4s after the PAGE started loading, not after Angular booted. */
  protected hintDelayMs = Math.max(0, 4000 - performance.now());

  protected retry(): void {
    // Drop any ?code=&state= from a failed exchange: a reused code always fails.
    window.location.assign(window.location.origin + window.location.pathname);
  }
}
