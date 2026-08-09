# MedGuard — Angular Frontend

Angular port of the Lovable-generated MedGuard design (same design tokens, same
component behavior), rebuilt as standalone Angular components with Tailwind CSS.

## Stack
- **Angular 22** (latest stable, released June 2026) — standalone components, signals, **zoneless change detection** (no `zone.js` — removed entirely; every piece of state in the app is a signal, wired via `provideZonelessChangeDetection()` in `app.config.ts`)
- Tailwind CSS 3 (design tokens ported 1:1 from the original `styles.css` — same
  OKLCH color values, same status semantics)
- `@lucide/angular` for icons (v1 — the `lucide-angular` package it replaces is deprecated. Icons are standalone components rendered dynamically via `<svg [lucideIcon]="ref">`, not a registered module — see `core/icons.ts` for the pattern used throughout the app)
- Leaflet for the map view
- Build system: `@angular/build` (the esbuild/Vite-based builder — no Webpack)

## Running it
```bash
npm install
npm start
```
(App scaffolded by hand rather than `ng new` — run `npm install` once before
first use; nothing else needed.)

## Structure

```
src/app/
  core/
    models/medguard.models.ts   — domain types (Batch, Alert, Shipment, Device...)
    status.ts                   — single source of truth for status label/color/icon
    format.ts                   — timeAgo, formatTemp, readingLevel, etc.
    mock-data.ts                — dummy dataset (7 batches, 3 alerts, 3 shipments, 5 devices)
    services/
      medguard-api.service.ts   — ⭐ THE file to edit when the real API is ready
  shared/components/            — reusable design-system pieces (StatusBadge, KpiCard,
                                   AppShell, DataTable, MapView, AlertFeed, EmptyState, Skeletons)
  features/
    dashboard/                  — ✅ DONE — fully wired to MedGuardApiService
    batches/                    — ⏳ placeholder — next to build
    batch-detail/                ⏳ placeholder
    monitoring/                  ⏳ placeholder
    alerts/                      ⏳ placeholder
    shipments/                   ⏳ placeholder
    devices/                     ⏳ placeholder
    settings/                    ⏳ placeholder
```

## Build order (one page at a time, as agreed)

1. ✅ **Dashboard** — done. KPI cards, unresolved alert feed, fleet map, recent batches table.
2. **Batches** (list) — filter chips, search, full table with pagination.
3. **Batch Detail** — the centerpiece: temperature chart with the safe-range band,
   alert history, shipment history, sensor reading log.
4. **Alerts** (full log) — filterable, bulk-resolve.
5. **Live Monitoring** — grid of live status cards, dark-mode-first.
6. **Shipments** — table + slide-out detail drawer with route map.
7. **Devices** — admin table, stale/offline detection.
8. **Settings** — org/profile page (lowest priority).

Say which one to do next and it'll be built the same way Dashboard was: signals
for state, `MedGuardApiService` for data, the shared component library for UI.

> **Zoneless note for new pages:** since there's no `zone.js`, always store
> component state in `signal()` (or update it via `computed()`) rather than
> plain class fields mutated outside a template event — that's what makes
> change detection fire reliably. `DashboardPageComponent` is the reference
> pattern for this.

## Connecting the real MedGuard .NET API

Everything goes through **`core/services/medguard-api.service.ts`** — components
never import mock data directly. To switch over:

1. Set `useDummyData: false` in `src/environments/environment.ts`, and point
   `apiBaseUrl` at your running `MedGuard.API` instance.
2. In `medguard-api.service.ts`, each method has the real `HttpClient` call
   already written out in a comment directly below the dummy version —
   uncomment it, delete the dummy line above it.
3. Note two backend gaps to close before `getDevices()` and the alert log's
   "all alerts" view can go live:
   - No `DeviceController` / `Device` entity exists yet server-side — only a
     bare `DeviceId` string on `SensorReading`. Add one before wiring Devices.
   - `AlertsController` only exposes `GET /alerts/unresolved` — add a
     `GET /alerts` (all, paginated) endpoint for the full Alerts log page.
4. `BatchStatus` is an `int` enum server-side vs. a string union
   (`"active" | "in_transit" | ...`) here — map it once in `mapBatchDto()`
   (already stubbed at the bottom of the service file) rather than mapping it
   per-component.

## Design tokens

`src/styles.css` and `tailwind.config.js` contain the exact same OKLCH color
values as the original Lovable `styles.css` — light mode, dark mode (`.dark`
class), and all five status tones (critical/warning/safe/transit/neutral) are
unchanged. If the source design system changes, those two files are the only
place to update.
