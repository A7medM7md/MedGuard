# MedGuard — Pharmaceutical Cold-Chain Traceability Platform

Tracks temperature-sensitive drug batches (vaccines, insulin) from manufacturer to
pharmacy, monitors live IoT temperature readings against each batch's safe range,
and automatically raises alerts / quarantines batches that breach it.

## Architecture — 4 Layers (Clean Architecture, no CQRS)

```
MedGuard.API            → Controllers, Program.cs, appsettings (composition root)
       ↓ depends on
MedGuard.Infrastructure → EF Core, DbContext, Repositories, migrations
       ↓ depends on
MedGuard.Application    → Services (business logic), DTOs, interfaces
       ↓ depends on
MedGuard.Domain         → Entities, enums, repository interfaces (zero external deps)
```

Dependency rule: **inner layers never reference outer layers.** Domain doesn't know
EF Core exists. Application doesn't know SQL Server exists — it only talks to
`IUnitOfWork` / repository interfaces defined in Domain. Infrastructure implements
those interfaces. API wires everything together in `Program.cs`.

## Core business rule (where the real logic lives)

`ColdChainMonitoringService.RecordReadingAsync` — every time a reading comes in:
1. Compare it against the batch's `MinSafeTemperatureC` / `MaxSafeTemperatureC`
2. If it's a small breach → raise a `Warning` alert
3. If it's a large breach → raise a `Critical` alert AND auto-quarantine the batch
   (`Batch.Status = Quarantined`), which blocks new shipments until reviewed

This single method is the heart of the whole system — everything else is CRUD
around it.

## Running locally

1. Update the connection string in `src/MedGuard.API/appsettings.json`
2. From `src/MedGuard.API`:
   ```bash
   dotnet ef migrations add InitialCreate -p ../MedGuard.Infrastructure -s .
   dotnet ef database update -p ../MedGuard.Infrastructure -s .
   dotnet run
   ```
3. Swagger UI at `https://localhost:xxxx/swagger`

## Suggested flow to test end-to-end

1. `POST /api/batches` — create a batch with a safe range, e.g. 2–8°C
2. `POST /api/sensorreadings` — post a reading of `10.5` for that batch →
   check the batch status flips to `Quarantined` and an alert appears in
   `GET /api/alerts/unresolved`
3. `POST /api/shipments` for that batch → should return 400 while quarantined
4. `POST /api/alerts/{id}/resolve` → resolve it, then retry the shipment

## Next steps (Phase 2 — splitting into microservices)

Once this monolith is solid, the natural service boundaries already exist because
the layers are clean:
- `ColdChainMonitoringService` → its own service, consuming a Kafka topic of raw
  sensor events instead of a direct controller call
- `BatchService` + `ShipmentService` → Batch-Traceability service
- Alerts → its own service publishing to SignalR / SMS / email
- Replace direct method calls between them with Kafka events + the Outbox pattern
  for consistency
