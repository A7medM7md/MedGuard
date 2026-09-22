/** AlertChannel flags enum on the server: None=0, InApp=1, Email=2, Sms=4 (bitwise-combinable). */
export const AlertChannelFlag = {
  InApp: 1,
  Email: 2,
  Sms: 4,
} as const;

export type AlertChannelKey = keyof typeof AlertChannelFlag;

export interface OrgSettings {
  organizationName: string;
  timeZone: string;
  warningMarginPercent: number;
  autoQuarantineOnBreach: boolean;
  criticalAlertChannels: number;
  warningAlertChannels: number;
  deviceSilentAfterMinutes: number;
}

/** Raw shape returned by MedGuard.API (GET/PUT /api/settings) — same fields, same casing (camelCase). */
export type SettingsDto = OrgSettings;

export function hasChannel(channels: number, flag: AlertChannelKey): boolean {
  return (channels & AlertChannelFlag[flag]) !== 0;
}

export function toggleChannel(channels: number, flag: AlertChannelKey, enabled: boolean): number {
  return enabled ? channels | AlertChannelFlag[flag] : channels & ~AlertChannelFlag[flag];
}
