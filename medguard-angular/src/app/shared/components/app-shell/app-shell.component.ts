import { Component, ElementRef, HostListener, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
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
import { BatchService } from '../../../core/services/batch.service';
import { DeviceService } from '../../../core/services/device.service';
import { AuthService } from '../../../core/services/auth.service';
import { Batch, Device } from '../../../core/models';
import { IconRef } from '../../../core/icons';
import { MedguardLogoComponent } from '../medguard-logo/medguard-logo';

const MAX_RESULTS_PER_GROUP = 5;

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
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive, RouterOutlet, LucideDynamicIcon, MedguardLogoComponent],
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

  // ------------------------------------------------------------ Search box
  private allBatches = signal<Batch[]>([]);
  private allDevices = signal<Device[]>([]);
  query = signal('');
  resultsOpen = signal(false);

  batchResults = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return [];
    return this.allBatches()
      .filter((b) => b.batchNumber.toLowerCase().includes(q) || b.drugName.toLowerCase().includes(q))
      .slice(0, MAX_RESULTS_PER_GROUP);
  });

  deviceResults = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return [];
    return this.allDevices()
      .filter((d) => d.deviceCode.toLowerCase().includes(q) || d.model.toLowerCase().includes(q))
      .slice(0, MAX_RESULTS_PER_GROUP);
  });

  hasResults = computed(() => this.batchResults().length > 0 || this.deviceResults().length > 0);

  constructor(
    private alertService: AlertService,
    private batchService: BatchService,
    private deviceService: DeviceService,
    private router: Router,
    private elementRef: ElementRef<HTMLElement>,
    public auth: AuthService,
  ) {}

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
    // Fetched once here rather than per-keystroke — both lists are small and
    // every other page already loads them the same way, so results are
    // instant and there's no per-character network chatter to debounce.
    this.batchService.getBatches().subscribe((batches) => this.allBatches.set(batches));
    this.deviceService.getDevices().subscribe((devices) => this.allDevices.set(devices));
  }

  toggleDark(): void {
    const next = !this.dark();
    this.dark.set(next);
    window.localStorage.setItem('medguard-theme', next ? 'dark' : 'light');
    document.documentElement.classList.toggle('dark', next);
  }

  onSearchInput(value: string): void {
    this.query.set(value);
    this.resultsOpen.set(value.trim().length > 0);
  }

  onSearchFocus(): void {
    if (this.query().trim().length > 0) this.resultsOpen.set(true);
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.closeSearch();
      (event.target as HTMLInputElement).blur();
    }
  }

  goToBatch(batch: Batch): void {
    this.closeSearch();
    this.router.navigate(['/batches', batch.id]);
  }

  goToDevice(device: Device): void {
    this.closeSearch();
    this.router.navigate(['/devices'], { queryParams: { device: device.id } });
  }

  private closeSearch(): void {
    this.query.set('');
    this.resultsOpen.set(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.resultsOpen() && !this.elementRef.nativeElement.contains(event.target as Node)) {
      this.resultsOpen.set(false);
    }
  }
}
