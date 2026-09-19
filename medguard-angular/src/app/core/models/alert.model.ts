export type AlertSeverity = 'critical' | 'warning';

export interface Alert {
  id: string;
  batchId: string;
  batchNumber: string;
  drugName: string;
  severity: AlertSeverity;
  message: string;
  triggeredAt: string;
  resolvedAt: string | null;
  resolvedBy: string | null;
}

/** AlertSeverity enum on the server: Info=0, Warning=1, Critical=2. Mapped to Alert by AlertService. */
export interface AlertDto {
  id: string;
  batchId: string;
  batchNumber: string;
  drugName: string;
  severity: number;
  message: string;
  triggeredAtUtc: string;
  isResolved: boolean;
  resolvedAtUtc: string | null;
}
