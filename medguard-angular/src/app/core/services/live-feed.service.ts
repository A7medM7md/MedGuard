import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BatchStatus, Reading } from '../models';
import { AuthService } from './auth.service';

/**
 * 'live'        connected AND heartbeats say the feed behind it is alive: no polling needed
 * 'stale'       connected, but heartbeats stopped or report Kafka down: poll (ADR 0010)
 * 'connecting'  first connect, or auto-reconnecting after a drop
 * 'offline'     no hub (production, notifier disabled), so the caller polls
 */
export type LiveFeedState = 'live' | 'stale' | 'connecting' | 'offline';

/** Mirrors FeedStatus in MedGuard.API/Realtime/MonitoringHub.cs. Sent every 5s. */
interface FeedStatus {
  kafkaHealthy: boolean;
  assignedPartitions: number;
  serverTimeUtc: string;
}

/** Three missed heartbeats (sent every 5s) and the feed counts as stale. */
const HEARTBEAT_TIMEOUT_MS = 15_000;

/** Mirrors AlertNotification in MedGuard.API/Realtime/MonitoringHub.cs. */
export interface LiveAlert {
  alertId: string;
  batchId: string;
  batchNumber: string;
  drugName: string;
  severity: 'Warning' | 'Critical';
  message: string;
  temperatureC: number;
  batchStatus: string;
  deviceCode: string;
  triggeredAtUtc: string;
  suppressedCount: number;
  /** The reading's trace id (ADR 0011): search it in Seq to see device -> browser. */
  traceId: string | null;
}

/** Mirrors LiveReading in MedGuard.API/Realtime/MonitoringHub.cs. */
interface LiveReadingDto {
  messageId: string;
  batchId: string;
  deviceCode: string;
  temperatureC: number;
  humidityPercent: number | null;
  latitude: number | null;
  longitude: number | null;
  recordedAtUtc: string;
}

const SERVER_BATCH_STATUS: Record<string, BatchStatus> = {
  Active: 'active',
  InTransit: 'in_transit',
  Delivered: 'delivered',
  Quarantined: 'quarantined',
  Recalled: 'recalled',
};

export function mapServerBatchStatus(status: string): BatchStatus | undefined {
  return SERVER_BATCH_STATUS[status];
}

/**
 * The browser end of ADR 0007: one SignalR connection, fed by the API's Kafka notifier.
 *
 * Connection state is a signal (the page renders it). The pushed messages are
 * Subjects: they're events, not state, and a signal would drop all but the last
 * one if two arrived before change detection ran.
 */
@Injectable({ providedIn: 'root' })
export class LiveFeedService {
  readonly state = signal<LiveFeedState>('offline');
  readonly alerts$ = new Subject<LiveAlert>();
  readonly readings$ = new Subject<Reading[]>();
  /** Fires after an automatic reconnect: pushes sent while we were away are lost, so resync over HTTP. */
  readonly reconnected$ = new Subject<void>();

  private connection?: HubConnection;
  private users = 0;
  private lastHeartbeat = 0;
  private feedHealthy = false;
  private watchdog?: ReturnType<typeof setInterval>;

  constructor(private auth: AuthService) {}

  /** Ref-counted: the connection stays open while at least one page uses it. */
  async connect(): Promise<void> {
    this.users++;
    const url = environment.monitoringHubUrl;
    if (!url || this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl(url, {
        // Called on every connect AND reconnect, so a silently refreshed token is
        // picked up. For WebSockets it's sent as ?access_token= (see Program.cs).
        accessTokenFactory: () => this.auth.accessToken ?? '',
        // The API's CORS policy is AllowAnyOrigin, which browsers refuse to
        // combine with credentials (cookies). We authenticate with a bearer
        // token, not cookies, so we don't need them.
        withCredentials: false,
      })
      // Retry delays after a dropped connection. After the last one it gives up and fires onclose.
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(environment.production ? LogLevel.Warning : LogLevel.Information)
      .build();

    connection.on('AlertRaised', (alert: LiveAlert) => this.alerts$.next(alert));
    connection.on('ReadingsUpdated', (readings: LiveReadingDto[]) => this.readings$.next(readings.map(toReading)));
    connection.on('FeedHeartbeat', (status: FeedStatus) => {
      this.lastHeartbeat = Date.now();
      const wasLive = this.state() === 'live';
      this.feedHealthy = status.kafkaHealthy;
      this.refreshState();
      // Coming back from 'stale': pushes were missed meanwhile, so resync like after a reconnect.
      if (!wasLive && this.state() === 'live') this.reconnected$.next();
    });

    connection.onreconnecting(() => this.state.set('connecting'));
    connection.onreconnected(() => {
      // Wait for the next heartbeat before calling it 'live' again.
      this.state.set('stale');
      this.reconnected$.next();
    });
    connection.onclose(() => {
      // Reconnect gave up (or we stopped). Forget this connection so the next
      // connect() builds a fresh one instead of returning early on a dead one.
      if (this.connection === connection) this.connection = undefined;
      this.stopWatchdog();
      this.state.set('offline');
    });

    this.connection = connection;
    this.state.set('connecting');
    try {
      await connection.start();
      // Connected isn't the same as fed: 'stale' until the first heartbeat arrives.
      this.state.set('stale');
      this.startWatchdog();
    } catch {
      // Hub not there (production, notifier disabled, API down). Not an error
      // for the user: the page simply keeps polling.
      this.state.set('offline');
      this.connection = undefined;
    }
  }

  async disconnect(): Promise<void> {
    this.users = Math.max(0, this.users - 1);
    if (this.users > 0 || !this.connection) return;

    const connection = this.connection;
    this.connection = undefined;
    this.stopWatchdog();
    if (connection.state !== HubConnectionState.Disconnected) await connection.stop();
    this.state.set('offline');
  }

  /** Recomputes live vs stale from the last heartbeat. Only while connected. */
  private refreshState(): void {
    if (this.connection?.state !== HubConnectionState.Connected) return;
    const fresh = Date.now() - this.lastHeartbeat < HEARTBEAT_TIMEOUT_MS;
    this.state.set(fresh && this.feedHealthy ? 'live' : 'stale');
  }

  /** Heartbeats can stop without any event (notifier loop stuck): check on a timer. */
  private startWatchdog(): void {
    this.stopWatchdog();
    this.watchdog = setInterval(() => this.refreshState(), 5_000);
  }

  private stopWatchdog(): void {
    if (this.watchdog) clearInterval(this.watchdog);
    this.watchdog = undefined;
  }
}

function toReading(dto: LiveReadingDto): Reading {
  return {
    id: dto.messageId,
    batchId: dto.batchId,
    deviceId: dto.deviceCode,
    temperatureC: dto.temperatureC,
    humidityPct: dto.humidityPercent ?? 0,
    lat: dto.latitude ?? 0,
    lng: dto.longitude ?? 0,
    recordedAt: dto.recordedAtUtc,
  };
}
