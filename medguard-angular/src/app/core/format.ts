import { Batch, Reading } from './models/medguard.models';

export function timeAgo(isoDate: string, now: number = Date.now()): string {
  const diff = Math.max(0, now - new Date(isoDate).getTime());
  const m = Math.floor(diff / 60000);
  if (m < 1) return 'just now';
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ${m % 60}m ago`;
  const d = Math.floor(h / 24);
  return `${d}d ago`;
}

const pad = (n: number) => String(n).padStart(2, '0');

export function formatDateTime(isoDate: string): string {
  const d = new Date(isoDate);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

export function formatDate(isoDate: string): string {
  const d = new Date(isoDate);
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

export function formatClock(isoDate: string): string {
  const d = new Date(isoDate);
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

export function formatTemp(value: number): string {
  return `${value.toFixed(1)} °C`;
}

export function daysUntil(isoDate: string, now: number = Date.now()): number {
  return Math.round((new Date(isoDate).getTime() - now) / 86400000);
}

export type ReadingLevel = 'safe' | 'warning' | 'critical';

/** Classifies a reading against the batch safe range: breach = critical, within 15% of an edge = warning. */
export function readingLevel(batch: Batch, reading: Reading | null): ReadingLevel {
  if (!reading) return 'warning';
  const { minTempC: min, maxTempC: max } = batch;
  const t = reading.temperatureC;
  if (t < min || t > max) return 'critical';
  const margin = (max - min) * 0.15;
  if (t < min + margin || t > max - margin) return 'warning';
  return 'safe';
}
