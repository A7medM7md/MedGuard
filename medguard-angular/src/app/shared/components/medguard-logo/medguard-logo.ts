import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-medguard-logo',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="medguard-brand-lockup" [class.icon-only]="iconOnly">
      <!-- Vector Shield & Cryo-Sensor Mark -->
      <svg
        [attr.width]="size"
        [attr.height]="size"
        viewBox="0 0 48 48"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        aria-hidden="true"
      >
        <defs>
          <linearGradient [id]="'mg-grad-' + uid" x1="4" y1="4" x2="44" y2="44" gradientUnits="userSpaceOnUse">
            <stop offset="0%" stop-color="#0B3B5C" />
            <stop offset="100%" stop-color="#0284C7" />
          </linearGradient>
          <linearGradient [id]="'core-glow-' + uid" x1="16" y1="16" x2="32" y2="32" gradientUnits="userSpaceOnUse">
            <stop offset="0%" stop-color="#38BDF8" />
            <stop offset="100%" stop-color="#0EA5E9" />
          </linearGradient>
        </defs>

        <!-- Outer Protective Shield -->
        <path
          d="M24 4L7 11V22C7 32.5 14.2 42.1 24 44.5C33.8 42.1 41 32.5 41 22V11L24 4Z"
          [attr.stroke]="'url(#mg-grad-' + uid + ')'"
          stroke-width="3"
          stroke-linejoin="round"
          fill="currentColor"
          fill-opacity="0.08"
        />

        <!-- Thermal Telemetry / Cryo Crystal Core -->
        <path
          d="M24 13V35M13 24H35M16.5 16.5L31.5 31.5M31.5 16.5L16.5 31.5"
          [attr.stroke]="'url(#core-glow-' + uid + ')'"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />

        <!-- Central IoT Sensor Hub -->
        <circle cx="24" cy="24" r="3.5" fill="#0284C7" />
        <circle cx="24" cy="24" r="1.5" fill="#FFFFFF" />

        <!-- Perimeter Telemetry Nodes -->
        <circle cx="24" cy="13" r="1.5" fill="#38BDF8" />
        <circle cx="24" cy="35" r="1.5" fill="#38BDF8" />
        <circle cx="13" cy="24" r="1.5" fill="#38BDF8" />
        <circle cx="35" cy="24" r="1.5" fill="#38BDF8" />
      </svg>

      <!-- Brand Typography (Optional) -->
      <div *ngIf="!iconOnly" class="brand-text">
        <span class="brand-title">MedGuard</span>
        <span class="brand-subtitle">COLD-CHAIN CONTROL</span>
      </div>
    </div>
  `,
  styles: [`
    .medguard-brand-lockup {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      font-family: system-ui, -apple-system, sans-serif;
      user-select: none;
    }

    .brand-text {
      display: flex;
      flex-direction: column;
      line-height: 1.1;
    }

    .brand-title {
      font-size: 15px;
      font-weight: 700;
      letter-spacing: -0.02em;
      color: var(--brand-text-color, #0F172A);
    }

    :host-context(.dark) .brand-title {
      color: #F8FAFC;
    }

    .brand-subtitle {
      font-size: 9px;
      font-weight: 600;
      letter-spacing: 0.12em;
      color: #64748B;
      font-family: 'JetBrains Mono', monospace, sans-serif;
      margin-top: 2px;
    }

    .icon-only .brand-text {
      display: none;
    }
  `]
})
export class MedguardLogoComponent {
  @Input() size = 32;
  @Input() iconOnly = false;

  // Unique ID so multiple instances on one page do not clash on gradient IDs
  readonly uid = Math.random().toString(36).substring(2, 8);
}
