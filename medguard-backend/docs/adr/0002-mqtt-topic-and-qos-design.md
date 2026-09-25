# ADR 0002 — MQTT topic tree, QoS levels and device presence

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

Devices need a topic layout, a delivery guarantee per message type, and a way
for the backend to know whether a device is connected, including when it
disappears without saying goodbye.

## Decision

**Topic tree:** one subtree per device, with the message type as the last level:

| Topic | QoS | Retained | Why |
|---|---|---|---|
| `medguard/devices/{code}/telemetry` | 1 | no | A lost reading could hide a breach. Duplicates are cheap to drop (`MessageId`). |
| `medguard/devices/{code}/heartbeat` | 0 | no | The next one comes in seconds, so losing one doesn't matter. |
| `medguard/devices/{code}/status` | 1 | **yes** | Late subscribers must see the current state immediately. |

- The device code goes in the **topic**, not only the payload. Subscribers can
  then filter with wildcards (`+/telemetry`, `SIM-0001/#`), and broker-level ACLs
  can later restrict each device to its own subtree.
- **QoS 2 (exactly once) is not used.** It costs a 4-packet handshake per message,
  and it only covers one hop (device ↔ broker). It does nothing for broker → Kafka →
  consumer. End-to-end exactly-once comes from idempotent consumers, not the protocol.

**Presence:** each device registers a **Last Will** (`status = offline`, retained)
when it connects, then publishes a retained `online`. A clean shutdown publishes
`offline` itself and sends DISCONNECT, which cancels the Will. A crash or network
loss makes the broker publish the Will: at once if the socket closes, or after
1.5 × keep-alive (15s → ~22s) if the link dies silently.

**Sessions:** `CleanStart = false` with a 1-hour session expiry, so a brief
reconnect resumes in-flight QoS 1 deliveries instead of dropping them.

**MQTT v5** is used for session expiry, reason codes, and user properties. User
properties will carry trace context in the observability phase.

## Consequences

- The backend can derive device online/offline from the broker, not only by
  inferring it from `LastSeenAtUtc`.
- Every telemetry consumer must be idempotent (`MessageId`).
- Local dev exposes the broker on host port **11883**. Windows reserves a port
  range covering 1883 on this machine, so WSL can't forward it.
