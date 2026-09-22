import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon, LucideBell, LucideBuilding2, LucideMoon, LucideRotateCcw, LucideSave, LucideThermometer } from '@lucide/angular';

import { SettingsService } from '../../core/services/settings.service';
import { AlertChannelKey, OrgSettings, hasChannel, toggleChannel } from '../../core/models';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

const TIME_ZONES = [
  'UTC',
  'Africa/Cairo',
  'Europe/Berlin',
  'Europe/London',
  'America/New_York',
  'America/Los_Angeles',
  'Asia/Dubai',
  'Asia/Kolkata',
];

const CHANNEL_OPTIONS: { key: AlertChannelKey; label: string }[] = [
  { key: 'InApp', label: 'In-app' },
  { key: 'Email', label: 'Email' },
  { key: 'Sms', label: 'SMS' },
];

@Component({
  selector: 'mg-settings-page',
  standalone: true,
  imports: [CommonModule, FormsModule, LucideDynamicIcon, PageHeaderComponent],
  templateUrl: './settings.component.html',
})
export class SettingsPageComponent implements OnInit {
  loading = signal(true);
  error = signal(false);
  saving = signal(false);
  saveError = signal<string | null>(null);
  savedAt = signal<number | null>(null);

  form: OrgSettings | null = null;
  private original: OrgSettings | null = null;

  timeZones = TIME_ZONES;
  channelOptions = CHANNEL_OPTIONS;

  Building2 = LucideBuilding2;
  Thermometer = LucideThermometer;
  Bell = LucideBell;
  Moon = LucideMoon;
  Save = LucideSave;
  RotateCcw = LucideRotateCcw;

  constructor(private settingsService: SettingsService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        this.form = { ...settings };
        this.original = { ...settings };
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  dirty(): boolean {
    return !!this.form && !!this.original && JSON.stringify(this.form) !== JSON.stringify(this.original);
  }

  reset(): void {
    if (this.original) this.form = { ...this.original };
    this.saveError.set(null);
  }

  hasCriticalChannel(key: AlertChannelKey): boolean {
    return !!this.form && hasChannel(this.form.criticalAlertChannels, key);
  }

  hasWarningChannel(key: AlertChannelKey): boolean {
    return !!this.form && hasChannel(this.form.warningAlertChannels, key);
  }

  toggleCriticalChannel(key: AlertChannelKey, event: Event): void {
    if (!this.form) return;
    const checked = (event.target as HTMLInputElement).checked;
    this.form.criticalAlertChannels = toggleChannel(this.form.criticalAlertChannels, key, checked);
  }

  toggleWarningChannel(key: AlertChannelKey, event: Event): void {
    if (!this.form) return;
    const checked = (event.target as HTMLInputElement).checked;
    this.form.warningAlertChannels = toggleChannel(this.form.warningAlertChannels, key, checked);
  }

  save(): void {
    if (!this.form) return;

    if (this.form.warningMarginPercent < 0 || this.form.warningMarginPercent > 100) {
      this.saveError.set('Warning margin must be between 0 and 100%.');
      return;
    }
    if (this.form.deviceSilentAfterMinutes <= 0) {
      this.saveError.set('Device silent after must be a positive number of minutes.');
      return;
    }
    if (!this.form.organizationName.trim()) {
      this.saveError.set('Organization name is required.');
      return;
    }

    this.saveError.set(null);
    this.saving.set(true);
    this.settingsService.updateSettings(this.form).subscribe({
      next: (settings) => {
        this.form = { ...settings };
        this.original = { ...settings };
        this.saving.set(false);
        this.savedAt.set(Date.now());
      },
      error: () => {
        this.saving.set(false);
        this.saveError.set('Could not save settings. Try again.');
      },
    });
  }
}
