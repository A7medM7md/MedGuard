import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  LucideDynamicIcon,
  LucideActivity,
  LucideBell,
  LucideChevronsLeft,
  LucideChevronsRight,
  LucideCpu,
  LucideLayoutDashboard,
  LucideLogOut,
  LucideMoon,
  LucidePackage,
  LucideSearch,
  LucideSettings,
  LucideShieldCheck,
  LucideSun,
  LucideTruck,
} from '@lucide/angular';
import { AlertService } from '../../../core/services/alert.service';
import { AuthService } from '../../../core/services/auth.service';
import { IconRef } from '../../../core/icons';

interface NavItem {
  to: string;
  label: string;
  icon: IconRef;
  exact?: boolean;
}

const NAV: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LucideLayoutDashboard, exact: true },
  { to: '/batches', label: 'Batches', icon: LucidePackage },
  { to: '/monitoring', label: 'Live Monitoring', icon: LucideActivity },
  { to: '/alerts', label: 'Alerts', icon: LucideBell },
  { to: '/shipments', label: 'Shipments', icon: LucideTruck },
  { to: '/devices', label: 'Devices', icon: LucideCpu },
];

@Component({
  selector: 'mg-app-shell',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet, LucideDynamicIcon],
  templateUrl: './app-shell.component.html',
})
export class AppShellComponent implements OnInit {
  nav = NAV;
  collapsed = signal(false);
  dark = signal(false);
  unresolvedCount = signal(0);

  ShieldCheck = LucideShieldCheck;
  Settings = LucideSettings;
  ChevronsLeft = LucideChevronsLeft;
  ChevronsRight = LucideChevronsRight;
  Search = LucideSearch;
  Sun = LucideSun;
  Moon = LucideMoon;
  LogOut = LucideLogOut;

  constructor(private alertService: AlertService, public auth: AuthService) {}

  initials(): string {
    const parts = this.auth.displayName.trim().split(/\s+/);
    return parts
      .slice(0, 2)
      .map((p) => p[0]?.toUpperCase() ?? '')
      .join('') || '?';
  }

  ngOnInit(): void {
    const stored = window.localStorage.getItem('medguard-theme');
    const initial = stored ? stored === 'dark' : window.matchMedia('(prefers-color-scheme: dark)').matches;
    this.dark.set(initial);
    document.documentElement.classList.toggle('dark', initial);

    this.alertService.getAlerts().subscribe((alerts) => {
      this.unresolvedCount.set(alerts.filter((a) => !a.resolvedAt).length);
    });
  }

  toggleDark(): void {
    const next = !this.dark();
    this.dark.set(next);
    window.localStorage.setItem('medguard-theme', next ? 'dark' : 'light');
    document.documentElement.classList.toggle('dark', next);
  }
}
