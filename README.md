<div align="center">

# 🧊 MedGuard
### Pharmaceutical Cold-Chain & Medication Traceability Platform

A cold-chain monitoring platform built with **ASP.NET Core**, **EF Core**, **SQL Server**, and **Angular** to track medication batches from manufacturer to pharmacy, detect temperature breaches the instant they happen, and automatically quarantine affected stock before it reaches a patient.

---

![.NET](https://img.shields.io/badge/.NET-8-purple)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-WebAPI-blue)
![Clean Architecture](https://img.shields.io/badge/Architecture-Clean%20Architecture-informational)
![EF Core](https://img.shields.io/badge/EF%20Core-SQL%20Server-red)
![Angular](https://img.shields.io/badge/Angular-22-dd0031)
![Signals](https://img.shields.io/badge/Angular-Zoneless%20%2B%20Signals-dd0031)
![Tailwind](https://img.shields.io/badge/TailwindCSS-UI-38bdf8)

**Status:** Backend API, core domain logic, and OAuth2/OIDC auth (Duende IdentityServer)
are complete and running end-to-end. Every Angular page — Dashboard, Batches, Batch
Detail, Devices, Alerts, Shipments, Live Monitoring and Settings — is fully wired to
the real, authenticated API, with org-configurable thresholds actually driving domain
behavior rather than sitting decorative on a settings screen. See
[Roadmap](#-roadmap) for the event-driven microservices phase planned next.

</div>

---

# 📖 Overview

In Egypt — and developing countries generally — cold-chain failure for medication is a
real, under-reported risk. A two-hour power cut at a warehouse, or a delivery vehicle
without working refrigeration, can silently ruin an entire batch of vaccines or insulin,
with nobody finding out until it's already been administered. The WHO has documented
this as a systemic problem, and it hits rural distribution networks hardest, where
cold-chain monitoring is often manual or nonexistent.

MedGuard closes that gap: every sensor reading is checked against a batch's safe
temperature range the moment it arrives. A small breach raises a warning. A large
breach raises a critical alert **and automatically quarantines the batch**, blocking
it from being shipped until a human resolves the alert.

---

# 🚀 Key Features

## 🌡️ Cold-Chain Monitoring

- Every sensor reading (temperature, humidity, GPS) is evaluated against the
  batch's safe range on ingestion — no batch job, no delay
- Two-tier breach severity: `Warning` (approaching the edge of the safe range)
  vs. `Critical` (outside it)
- **Automatic quarantine** on critical breach — enforced inside the `Batch`
  aggregate itself, so it cannot be bypassed by any code path that records a
  reading
- Full sensor-reading history per batch

## 📦 Batch Traceability

- Register a batch with drug name, manufacturer, quantity, expiry and a safe
  temperature range
- One call returns a batch's full picture: recent readings, alerts, and
  shipment history together
- Status lifecycle enforced by the domain: `Active → InTransit → Delivered`,
  with `Quarantined`/`Recalled` as guarded side branches — illegal transitions
  (e.g. shipping a quarantined batch) are rejected at the domain layer, not
  patched over in a controller

## 🚨 Alerting

- Unresolved-alerts feed, resolved server-side (a resolution is a persisted
  fact — not client-side UI state that disappears when you change page)
- Paginated, filterable alert log by severity/resolved status
- Critical-alert resolution requires explicit confirmation in the UI —
  resolving cold-chain-breach alerts isn't a one-click, undo-able action

## 🚚 Shipment Tracking

- Ship a batch (blocked while quarantined or recalled — enforced by the
  aggregate)
- Mark a shipment delivered
- Full shipment log across every batch, plus per-batch shipment history

## 📡 Device Management

- Register an IoT sensor device
- Heartbeat endpoint (battery %, signal %) — device status
  (`Online` / `Stale` / `Offline`) is **derived from time-since-last-heartbeat**
  on every read, never stored, so a device can't silently stay "online" in the
  database after it's gone dark
- Assign / unassign a device to a batch

## 🖥️ Live Operations Dashboard (Angular)

- KPI cards (active batches, in-transit, unresolved alerts, quarantined) fed
  from the real API
- Unresolved-alert feed with resolve actions and critical-alert confirmation
- Recent-batches table with live status and last-reading temperature

## 📡 Live Monitoring

- Wall-display card grid of every batch currently in storage or transit,
  polling the API every 10 seconds
- Problem batches (critical, then warning) automatically sort to the top
- Per-batch sparkline trend and current reading, derived from the same sensor
  history the batch detail chart uses

## ⚙️ Settings — org-configurable, not decorative

- Organization profile, cold-chain thresholds and alert routing are real,
  persisted, and editable — not the read-only mock the original design shipped
  with
- Two settings actually drive live domain behavior: **Warning margin %**
  (replaces a hardcoded tolerance in `Batch.RecordReading` with a
  percentage-of-safe-range calculation) and **Auto-quarantine on breach** (a
  real on/off switch on the aggregate's quarantine call) — plus **Device
  silent after** controls when a device's derived status flips to Stale

---

# ⚙️ Request Pipeline (today)

```
IoT Device / Gateway
        │
        ▼
POST /api/sensorreadings
        │
        ▼
ColdChainMonitoringService
        │
        ▼
   Batch.RecordReading()  ← domain aggregate, owns the safety invariant
        │
        ├── within range ─────────────► stored, nothing else happens
        │
        ├── small breach ─────────────► Warning Alert raised
        │
        └── large breach ─────────────► Critical Alert raised
                                         + Batch auto-quarantined
        │
        ▼
   SQL Server (EF Core)
        │
        ▼
GET /api/alerts/unresolved ──► Angular Dashboard
```

---

# 🏛 Architecture — Clean Architecture (4 layers, no CQRS)

```
MedGuard
│
├── MedGuard.API              → Controllers, Program.cs, appsettings (composition root)
│         ↓ depends on
├── MedGuard.Infrastructure   → EF Core, DbContext, Repositories, migrations
│         ↓ depends on
├── MedGuard.Application      → Services (business logic), DTOs, interfaces
│         ↓ depends on
└── MedGuard.Domain           → Entities, enums, repository interfaces (zero external deps)
```

**Dependency rule: inner layers never reference outer layers.** `Domain` doesn't know
EF Core exists. `Application` doesn't know SQL Server exists — it only talks to
`IUnitOfWork` / repository interfaces defined in `Domain`. `Infrastructure` implements
those interfaces. `API` wires everything together in `Program.cs`.

This is a monolith today, on purpose. The service boundaries in the
[Roadmap](#-roadmap) already exist as clean, independently-testable layers inside it —
splitting them out later is a refactor, not a rewrite.

---

# 💾 Data Storage

Currently a single **SQL Server** database via EF Core, storing:

- Batches, sensor readings, alerts, shipments, devices
- Full relational history — every reading and every alert a batch has ever
  had, queryable per-batch in one round trip

A dedicated time-series store (TimescaleDB) for high-volume telemetry is
planned once ingestion volume moves past what a relational table comfortably
handles at scale — see [Roadmap](#-roadmap).

---

# 🛠 Tech Stack

| Category | Technology |
|-----------|------------|
| Backend | ASP.NET Core (.NET 8) |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / API) |
| Database | SQL Server (EF Core, code-first migrations) |
| Identity | Duende IdentityServer — OAuth2/OIDC, Authorization Code + PKCE |
| Frontend | Angular 22 — standalone components, zoneless change detection, signals |
| Styling | Tailwind CSS |
| Icons | Lucide |
| Maps | Leaflet |
| API contract | Consistent `Response<T>` envelope across every endpoint |

---

# 🔒 Security

MedGuard.API is an OAuth2 resource server: every endpoint requires a valid JWT
access token (checked at `BaseApiController` level, so new controllers are locked
down by default rather than by remembering to add `[Authorize]`), except ones
explicitly opted out with `[AllowAnonymous]` — currently just the device heartbeat
endpoint, which a physical sensor gateway calls and will get its own per-device key
scheme later.

**MedGuard.IdentityServer** — a Duende IdentityServer instance, run as its own
ASP.NET Core project — issues the tokens:
- Angular authenticates via **Authorization Code + PKCE**, the only OIDC flow
  considered safe for a browser-based SPA (no client secret embedded in JS)
- A separate `client_credentials` client exists for service-to-service calls
  (health checks, and later an IoT gateway's own service account)
- Two seeded demo accounts for now (`admin@medguard.local` / `operator@medguard.local`,
  password `Demo@123`) via Duende's in-memory test-user store — not a real user
  database yet; see [Roadmap](#-roadmap)

Rate limiting and secrets management (beyond `appsettings.json`) are still open
items — see [Roadmap](#-roadmap).

---

# ▶️ Running Locally

Three processes, in this order:

```bash
# 1. IdentityServer (issues tokens) — https://localhost:5001
cd medguard-backend/src/MedGuard.IdentityServer && dotnet run

# 2. API (resource server) — http://localhost:7591
cd medguard-backend/src/MedGuard.API && dotnet run --urls "http://localhost:7591"

# 3. Angular app — http://localhost:4200
cd medguard-angular && npm install && npm start
```

Opening `http://localhost:4200` redirects straight to IdentityServer's login page.
Demo accounts (seeded test users, not a real user database yet):

| Username | Password | Role |
|---|---|---|
| `admin@medguard.local` | `Demo@123` | Admin |
| `operator@medguard.local` | `Demo@123` | Operator |

---

# 📡 Example Request

```json
POST /api/sensorreadings
{
  "batchId": "c24e370d-8d0b-48fd-a7d2-06bab6d6f623",
  "deviceId": "DEV-CRYO-0142",
  "temperatureC": 10.5,
  "humidityPercent": 45,
  "latitude": 30.0444,
  "longitude": 31.2357
}
```

A reading like this against a batch with a 2–8°C safe range returns `200 OK`, flips the
batch's status to `Quarantined`, and raises a `Critical` alert visible immediately at
`GET /api/alerts/unresolved`.

---

# 📷 Screenshots

_Coming soon — Dashboard and Devices pages are live against the real API; screenshots
will be added here once the remaining frontend pages are built out._

---

# ⭐ Highlights

- Domain-driven safety invariant: auto-quarantine is enforced **inside the
  aggregate**, not in a service or controller, so it can't be bypassed
- Clean Architecture with strict dependency direction, ready to split into
  services without a rewrite
- Zoneless Angular 22 with signals — no `zone.js`, change detection driven
  entirely by signal state
- Consistent, typed API response envelope across every endpoint
- Derived (not stored) device status — correctness by construction instead of
  a background job keeping state in sync

---

# 🗺 Roadmap

Once the Angular frontend is feature-complete, MedGuard moves from a well-structured
monolith to a genuinely distributed, event-driven system. This is deliberately
sequenced — not "everything at once" — and each phase is documented with an
Architecture Decision Record as it lands.

### 1. Event-driven backbone (Kafka)
- `SensorReadings` topic for high-throughput, decoupled telemetry ingestion
- `BatchAlerts` and `ShipmentEvents` topics for state-change events
- **Outbox pattern** so a service's database write and its published event can
  never go out of sync on a crash
- **Idempotent consumers** everywhere — every consumer assumes redelivery is
  possible

### 2. Real bounded contexts (separate deployable services)
```
Cold-Chain-Monitoring Service   — ingests IoT telemetry, evaluates thresholds
Batch-Traceability Service      — tracks a batch manufacturer → pharmacy
Alerting-Notification Service   — SignalR live push + SMS/email escalation
Inventory-Distribution Service  — batch state machine (Active/Quarantined/Recalled)
Gateway (YARP)                  — API composition + auth at the edge
```

### 3. Resilience engineering
- Polly circuit breakers, retry-with-backoff, and bulkhead isolation on every
  inter-service call
- `/health/live` and `/health/ready` per service
- Graceful degradation as a design constraint (e.g. monitoring keeps ingesting
  and queues alerts if the notification service is down)

### 4. Observability
- OpenTelemetry distributed tracing across service boundaries
- Serilog structured logging into Seq/ELK
- Prometheus + Grafana dashboards

### 5. Production-grade security
- ✅ OAuth2/OIDC via Duende IdentityServer (Authorization Code + PKCE) — done
- Replace the seeded test-user store with a real persistent user store
  (ASP.NET Core Identity) plus self-service registration
- Rate limiting at the Gateway
- Proper secrets management

### 6. Infrastructure as code
- Docker Compose for local dev, Kubernetes manifests for deployment
- GitHub Actions: build → test → containerize → deploy

### 7. Testing strategy
- Testcontainers for integration tests against real SQL Server + Kafka in CI
- Contract testing between services

---

# 👨‍💻 Author

Built by **Ahmed Elgebaly**.
