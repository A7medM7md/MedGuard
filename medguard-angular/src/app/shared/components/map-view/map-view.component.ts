import { AfterViewInit, Component, ElementRef, Input, OnChanges, OnDestroy, SimpleChanges, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import type * as L from 'leaflet';
import { StatusTone } from '../../../core/status';

export interface MapPinData {
  id: string;
  lat: number;
  lng: number;
  label: string;
  sublabel?: string;
  tone: StatusTone;
  geofence?: number; // meters
}

const TONE_HEX: Record<StatusTone, string> = {
  critical: '#dc2626',
  warning: '#d97706',
  safe: '#15803d',
  transit: '#1470ad',
  neutral: '#57534e',
};

@Component({
  selector: 'mg-map-view',
  standalone: true,
  imports: [CommonModule],
  template: `<div class="mg-map overflow-hidden rounded-md border border-border" [style.height.px]="height">
    <div #mapEl class="h-full w-full"></div>
  </div>`,
})
export class MapViewComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() pins: MapPinData[] = [];
  @Input() height = 300;

  @ViewChild('mapEl') mapEl!: ElementRef<HTMLDivElement>;

  private map?: L.Map;
  private markers: L.Layer[] = [];
  private leaflet?: typeof L;

  async ngAfterViewInit(): Promise<void> {
    // Dynamic import keeps Leaflet out of the initial bundle and avoids SSR issues.
    const leaflet = await import('leaflet');
    this.leaflet = leaflet;

    this.map = leaflet.map(this.mapEl.nativeElement, {
      center: [50.5, 8.0],
      zoom: 5,
      zoomControl: true,
      attributionControl: true,
    });

    leaflet.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap contributors',
    }).addTo(this.map);

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

    this.markers.forEach((m) => this.map!.removeLayer(m));
    this.markers = [];

    this.pins.forEach((pin) => {
      const color = TONE_HEX[pin.tone];
      const icon = L.divIcon({
        className: '',
        html: `<span class="mg-pin-dot ${pin.tone === 'critical' ? 'mg-pin-pulse' : ''}" style="background:${color}; color:${color}"></span>`,
        iconSize: [14, 14],
      });
      const marker = L.marker([pin.lat, pin.lng], { icon }).addTo(this.map!);
      marker.bindTooltip(
        `<span class="mg-tip-title">${pin.label}</span>${pin.sublabel ? `<span class="mg-tip-sub">${pin.sublabel}</span>` : ''}`,
        { className: 'mg-tip', direction: 'top', offset: [0, -8] },
      );
      this.markers.push(marker);

      if (pin.geofence) {
        const circle = L.circle([pin.lat, pin.lng], {
          radius: pin.geofence,
          color,
          fillColor: color,
          fillOpacity: 0.06,
          weight: 1,
        }).addTo(this.map!);
        this.markers.push(circle);
      }
    });

    if (this.pins.length > 0) {
      const bounds = L.latLngBounds(this.pins.map((p) => [p.lat, p.lng] as [number, number]));
      this.map.fitBounds(bounds.pad(0.3));
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }
}
