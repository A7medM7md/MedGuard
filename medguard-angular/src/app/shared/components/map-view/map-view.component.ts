import {
  AfterViewInit,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import type * as L from 'leaflet';
import { StatusTone } from '../../../core/status';

export interface MapPinData {
  id: string;
  lat: number;
  lng: number;
  label: string;
  sublabel?: string;
  /** Extra tooltip lines (route, courier, last seen...). Plain text — escaped here. */
  details?: string[];
  tone: StatusTone;
  geofence?: number; // meters
  /** Recent positions, oldest first, drawn as a dashed breadcrumb behind the pin. */
  trail?: [number, number][];
  /** Pulse the pin (a live breach). Defaults to tone === 'critical'. */
  pulse?: boolean;
}

const TONE_HEX: Record<StatusTone, string> = {
  critical: '#dc2626',
  warning: '#d97706',
  safe: '#15803d',
  transit: '#1470ad',
  neutral: '#57534e',
};

const escapeHtml = (s: string) =>
  s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

@Component({
  selector: 'mg-map-view',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './map-view.component.html',
})
export class MapViewComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() pins: MapPinData[] = [];
  @Input() height = 300;
  /** Initial view when there are no pins to fit (default: Egypt, where the fleet runs). */
  @Input() center: [number, number] = [27.2, 30.8];
  @Input() zoom = 5;
  @Input() ariaLabel = 'Map';

  @Output() pinClick = new EventEmitter<string>();

  @ViewChild('mapEl') mapEl!: ElementRef<HTMLDivElement>;

  private map?: L.Map;
  private layers: L.Layer[] = [];
  private leaflet?: typeof L;
  /** The pin ids last fitted to, so a data refresh doesn't undo the user's pan/zoom. */
  private fittedKey = '';

  async ngAfterViewInit(): Promise<void> {
    // Dynamic import keeps Leaflet out of the initial bundle and avoids SSR issues.
    // Leaflet is CommonJS: the bundler puts its API on `default`, not on the namespace.
    const module = await import('leaflet');
    const leaflet = ((module as unknown as { default?: typeof L }).default ?? module) as typeof L;
    this.leaflet = leaflet;

    this.map = leaflet.map(this.mapEl.nativeElement, {
      center: this.center,
      zoom: this.zoom,
      zoomControl: true,
      attributionControl: true,
      scrollWheelZoom: false, // a dashboard scrolls; don't hijack the wheel
    });

    leaflet
      .tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors',
      })
      .addTo(this.map);

    this.renderPins();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['pins'] && this.map && this.leaflet) {
      this.renderPins();
    }
  }

  private renderPins(): void {
    if (!this.map || !this.leaflet) return;
    const L = this.leaflet;

    this.layers.forEach((layer) => this.map!.removeLayer(layer));
    this.layers = [];

    for (const pin of this.pins) {
      const color = TONE_HEX[pin.tone];

      if (pin.trail && pin.trail.length > 1) {
        this.layers.push(
          L.polyline(pin.trail, { color, weight: 3, opacity: 0.55, dashArray: '4 6', interactive: false }).addTo(this.map),
        );
      }
      if (pin.geofence) {
        this.layers.push(
          L.circle([pin.lat, pin.lng], {
            radius: pin.geofence,
            color,
            fillColor: color,
            fillOpacity: 0.08,
            weight: 1,
            interactive: false,
          }).addTo(this.map),
        );
      }

      const pulse = pin.pulse ?? pin.tone === 'critical';
      const icon = L.divIcon({
        className: '',
        html: `<span class="mg-pin-dot ${pulse ? 'mg-pin-pulse' : ''}" style="background:${color}; color:${color}"></span>`,
        iconSize: [14, 14],
      });
      const marker = L.marker([pin.lat, pin.lng], { icon, keyboard: true, title: pin.label }).addTo(this.map);
      const lines = [pin.sublabel, ...(pin.details ?? [])].filter((l): l is string => !!l);
      marker.bindTooltip(
        `<span class="mg-tip-title">${escapeHtml(pin.label)}</span>` +
          lines.map((l) => `<span class="mg-tip-sub">${escapeHtml(l)}</span>`).join(''),
        { className: 'mg-tip', direction: 'top', offset: [0, -8] },
      );
      marker.on('click', () => this.pinClick.emit(pin.id));
      this.layers.push(marker);
    }

    const key = this.pins.map((p) => p.id).sort().join('|');
    if (this.pins.length > 0 && key !== this.fittedKey) {
      const points = this.pins.flatMap((p) => [[p.lat, p.lng] as [number, number], ...(p.trail ?? [])]);
      this.map.fitBounds(L.latLngBounds(points).pad(0.25), { maxZoom: 9 });
      this.fittedKey = key;
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }
}
