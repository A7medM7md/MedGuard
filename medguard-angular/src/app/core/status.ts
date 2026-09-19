import {
  LucideAlertTriangle,
  LucideBan,
  LucideCheckCircle2,
  LucideCircleSlash,
  LucideClock,
  LucidePackageCheck,
  LucideRadio,
  LucideShieldAlert,
  LucideSnowflake,
  LucideTruck,
  LucideWifiOff,
} from '@lucide/angular';
import { AlertSeverity, BatchStatus, DeviceStatus, ShipmentStatus } from './models';
import { IconRef } from './icons';

export type StatusTone = 'critical' | 'warning' | 'safe' | 'transit' | 'neutral';

export interface StatusMeta {
  label: string;
  tone: StatusTone;
  icon: IconRef;
}

/** Single source of truth for every status label / color / icon in the app. */
export const batchStatusMeta: Record<BatchStatus, StatusMeta> = {
  active: { label: 'Active', tone: 'safe', icon: LucideSnowflake },
  in_transit: { label: 'In Transit', tone: 'transit', icon: LucideTruck },
  quarantined: { label: 'Quarantined', tone: 'critical', icon: LucideShieldAlert },
  delivered: { label: 'Delivered', tone: 'safe', icon: LucidePackageCheck },
  recalled: { label: 'Recalled', tone: 'neutral', icon: LucideBan },
};

export const alertSeverityMeta: Record<AlertSeverity, StatusMeta> = {
  critical: { label: 'Critical', tone: 'critical', icon: LucideAlertTriangle },
  warning: { label: 'Warning', tone: 'warning', icon: LucideAlertTriangle },
};

export const shipmentStatusMeta: Record<ShipmentStatus, StatusMeta> = {
  preparing: { label: 'Preparing', tone: 'neutral', icon: LucideClock },
  in_transit: { label: 'In Transit', tone: 'transit', icon: LucideTruck },
  delivered: { label: 'Delivered', tone: 'safe', icon: LucidePackageCheck },
  aborted: { label: 'Aborted', tone: 'critical', icon: LucideAlertTriangle },
};

export const deviceStatusMeta: Record<DeviceStatus, StatusMeta> = {
  online: { label: 'Online', tone: 'safe', icon: LucideRadio },
  stale: { label: 'Silent', tone: 'warning', icon: LucideCircleSlash },
  offline: { label: 'Offline', tone: 'critical', icon: LucideWifiOff },
};

export const readingLevelMeta: Record<'safe' | 'warning' | 'critical', StatusMeta> = {
  safe: { label: 'In range', tone: 'safe', icon: LucideCheckCircle2 },
  warning: { label: 'Near limit', tone: 'warning', icon: LucideAlertTriangle },
  critical: { label: 'Out of range', tone: 'critical', icon: LucideAlertTriangle },
};

export const toneText: Record<StatusTone, string> = {
  critical: 'text-critical',
  warning: 'text-warning',
  safe: 'text-safe',
  transit: 'text-transit',
  neutral: 'text-neutralst',
};

export const toneBg: Record<StatusTone, string> = {
  critical: 'bg-critical-bg',
  warning: 'bg-warning-bg',
  safe: 'bg-safe-bg',
  transit: 'bg-transit-bg',
  neutral: 'bg-neutralst-bg',
};

export const toneBorder: Record<StatusTone, string> = {
  critical: 'border-critical-border',
  warning: 'border-warning-border',
  safe: 'border-safe-border',
  transit: 'border-transit-border',
  neutral: 'border-neutralst-border',
};

/** 4px left accent bar for status-bearing cards/rows. */
export const toneAccent: Record<StatusTone, string> = {
  critical: 'border-l-critical',
  warning: 'border-l-warning',
  safe: 'border-l-safe',
  transit: 'border-l-transit',
  neutral: 'border-l-neutralst',
};
