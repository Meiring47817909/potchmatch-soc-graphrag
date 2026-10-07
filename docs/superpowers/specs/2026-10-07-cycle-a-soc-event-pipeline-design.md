# Cycle A — SOC Event Pipeline, Detection Rules and Alerts (Design)

**Project:** PotchMatch (ITRI623 Phase 2)
**Cycle:** A of 3 (A: events/rules/alerts → B: Neo4j knowledge graph → C: GraphRAG + admin SOC page)
**Covers spec sections:** §3.1 SOC Event Collection, §3.2 SOC Storage, §3.3 Threat Detection Rules, §3.4 Alert Service
**Date:** 2026-10-07
**Status:** Approved in conversation, awaiting written-spec review

---

## 1. Goal and success criteria

Collect security and operational events from every Phase 1 service into one store, run at least three detection rules over that store, and produce alerts in the Phase 2 spec's alert shape, each linked to the events that caused it.

Cycle A is done when:

1. Events from the API Gateway, IdentityService, BookingService and PaymentService, plus service health status, are stored in `soc_db` with every §3.1 field present where applicable (`eventId`, `timestamp`, `serviceName`, `eventType`, `severity`, `userId`, `sourceIp`, `endpoint`, `httpMethod`, `statusCode`, `message`, `correlationId`, `affectedEntity`).
2. The §7 demonstration workflow runs end to end up to step 5: repeated failed logins → `FAILED_LOGIN` events recorded → events collected → Rule 1 fires → a HIGH alert exists whose `relatedEvents` lists the exact triggering events.
3. All four rules in §5 can be triggered on demand and produce the documented alert.
4. Alerts are retrievable through admin-only APIs in the spec's exact JSON shape, ready for Cycle B to load into Neo4j.

## 2. Constraints

- **Phase 1 is never modified.** All work happens in the copy `squash-platform-phase2` (git-initialised, baseline commit `ee51deb`). The original `squash-platform` folder stays exactly as submitted.
- Same stack as Phase 1: .NET 10 Web APIs, EF Core + PostgreSQL, Serilog, Ocelot, Consul, Docker Compose. Everything still starts with one `docker compose up`.
- Database-per-service continues: SocService owns `soc_db`; no service reads another's database, including Cycle B reading SocService only through its API.
- Explainability matters (spec §12 "Technical explanation" criterion), so rule logic and thresholds must be documented from the code itself.

## 3. Out of scope for Cycle A

- Neo4j, graph loading, and spec Rule 5 (Suspicious Graph Query) → Cycle B/C.
- The admin SOC page in Angular → Cycle C. In Cycle A, alerts are inspected through the API (Postman).
- Better Stack or any third-party log platform (decision: own SOC service + Postgres instead).
- Active response (actually blocking IPs). Rules *recommend* actions; they don't enforce them.

## 4. Architecture

### 4.1 New service: `SocService`

- .NET 10 Web API in `backend/src/SocService`, same project conventions as the other services (Serilog, Consul self-registration with a `/health` check, EF Core migrations created **with** their `.Designer.cs` files — see the Phase 1 lesson in `handout.md` §6).
- Own database `soc_db` in a new `soc-db` Postgres container with its own volume.
- Two background workers: the **Consul health poller** (§4.4) and the **detection engine** (§5).

### 4.2 Changes to existing services

1. **`SecurityEventLog.Emit()`** (all four copies) adds:
   - `EventId` — a new GUID on every call;
   - a `SecurityEvent=true` marker property so the HTTP sink can select only these events.
2. **Real client IP.** The gateway adds an `X-Forwarded-For` header containing the caller's IP to every downstream request. Each service enables ASP.NET Core forwarded-headers handling so `HttpContext.Connection.RemoteIpAddress` is the client's IP, not the gateway container's. Every `Emit()` call that has an `HttpContext` passes `sourceIp`; call sites that currently omit it are updated.
3. **Serilog HTTP sink.** Each service adds a sub-logger with `Serilog.Sinks.Http`, filtered to events with `SecurityEvent=true`, posting batches to `http://soc-service:8080/api/events` with header `X-Soc-Ingest-Key`. It uses the sink's durable (disk-buffered) mode so events survive SocService being down. Existing stdout logging is unchanged.

### 4.3 Data flow

```
Gateway / Identity / Booking / Payment
   └─ SecurityEventLog.Emit() ──► stdout (unchanged)
                               └─► Serilog HTTP sink (durable) ──► SocService POST /api/events ──► soc_db.events
Consul health API ◄── poll every 5s ── SocService health poller ──► soc_db.events
                                          │
                     detection engine (every 15s) ──► soc_db.alerts + soc_db.alert_events
Admin (Postman now, Angular in Cycle C) ──► Gateway /api/soc/* (Admin role) ──► SocService read APIs
```

### 4.4 Service health events

The health poller calls Consul's `/v1/health/state/any` every 5 seconds and keeps the last known status per service in memory. On a transition it stores:

- `SERVICE_HEALTH_CRITICAL` (severity HIGH) when a service goes passing → critical;
- `SERVICE_HEALTH_RECOVERED` (severity LOW) when it goes critical → passing.

`serviceName` is `soc-service`; `affectedEntity` is the affected service's name (e.g. `payment-service`). On first poll after startup, current state is recorded as the baseline with no events. If Consul itself is unreachable, the failure is logged and **no** health events are recorded.

## 5. Detection rules

Each rule is a class implementing `IDetectionRule` and exposes the §11.4 documentation fields as properties: `RuleName`, `Purpose`, `InputEventTypes`, `DetectionCondition`, `Severity`, `AlertGenerated`, `RecommendedResponse`. Thresholds and windows come from `appsettings.json` (`DetectionRules` section) so they can be lowered for a demo without code changes.

The **detection engine** runs every 15 seconds. For each rule it loads the rule's input events within that rule's window, asks the rule for matches (each match = grouping key + triggering events + alert fields), and passes matches to the alert writer (§6.3). A rule that throws is logged and skipped; the others still run that cycle.

| # | Rule name | Input events | Condition (defaults) | Severity | Alert `ruleName` / dedup key | Recommended response |
|---|---|---|---|---|---|---|
| 1 | Multiple Failed Logins | `FAILED_LOGIN` | ≥5 within 5 min for the same target account (`userId` = attempted email) **or** for the same `sourceIp` — two independent groupings | HIGH | `Multiple Failed Logins` / `Multiple Failed Logins:account:{email}` or `…:ip:{ip}` | Temporarily block the source IP and verify whether the account is compromised; consider forcing a password reset. |
| 2 | Unauthorised Endpoint Access | `AUTH_TOKEN_REJECTED`, `UNAUTHORISED_ACCESS` | ≥3 within 5 min for the same `userId`; events with no `userId` group by `sourceIp` | MEDIUM | `Unauthorised Endpoint Access` / `…:user:{id}` or `…:ip:{ip}` | Review the user's role assignments and recent activity; revoke the session if access attempts continue. |
| 3 | Abnormal Request Rate | `REQUEST_RECEIVED` where `serviceName = api-gateway` | >100 within 60 s for the same `sourceIp` | HIGH | `POSSIBLE_DOS` / `POSSIBLE_DOS:ip:{ip}` | Rate-limit or temporarily block the source IP at the gateway. |
| 4 | Service Failure | `SERVICE_HEALTH_CRITICAL`, `INTER_SERVICE_CALL_FAILED`, `UNHANDLED_EXCEPTION` | Any `SERVICE_HEALTH_CRITICAL` (fires immediately), **or** ≥3 call failures/exceptions with the same affected service within 2 min | HIGH | `SERVICE_FAILURE` / `SERVICE_FAILURE:{service}` | Check the affected service's container and logs; dependent services are degraded until it recovers. |

**Rule 4 auto-resolve:** when a `SERVICE_HEALTH_RECOVERED` event arrives for a service with an `OPEN` or `ACKNOWLEDGED` `SERVICE_FAILURE` alert, that alert is set to `RESOLVED`.

**Affected service for Rule 4:** for health events, `affectedEntity`; for `INTER_SERVICE_CALL_FAILED`, `affectedEntity` (already the target service, e.g. `payment-service`); for `UNHANDLED_EXCEPTION`, the event's own `serviceName`.

**Known limitation (to state in the report):** locally, every request from the host reaches Docker from the same bridge IP, so Rule 3 effectively measures total traffic from the host rather than per real client.

## 6. Data model (`soc_db`)

### 6.1 `events`

| Column | Type | Notes |
|---|---|---|
| `event_id` | uuid PK | from `Emit()`; ingestion ignores duplicates (idempotent) |
| `timestamp` | timestamptz | when the event happened |
| `received_at` | timestamptz | when SocService stored it |
| `service_name` | text | |
| `event_type` | text | |
| `severity` | text | LOW / MEDIUM / HIGH |
| `user_id`, `source_ip`, `endpoint`, `http_method` | text, nullable | |
| `status_code` | int, nullable | |
| `message` | text | |
| `correlation_id`, `affected_entity` | text, nullable | |

Indexes: `(event_type, timestamp)`, `(source_ip, timestamp)`, `(user_id, timestamp)`.

### 6.2 `alerts` and `alert_events`

`alerts`: `alert_id` (uuid PK), `timestamp`, `rule_name`, `severity`, `status` (`OPEN` / `ACKNOWLEDGED` / `RESOLVED`), `description`, `affected_service`, `affected_user`, `affected_entity`, `recommended_action`, `dedup_key`, `updated_at`.

`alert_events`: `alert_id` + `event_id` (composite PK) — the spec's `relatedEvents`.

### 6.3 Alert writing and deduplication

For each rule match: if an alert with the same `dedup_key` exists in `OPEN` or `ACKNOWLEDGED` status, the match's events are added to that alert's `alert_events` (existing links ignored) and `updated_at` is set; otherwise a new `OPEN` alert is created with those events. A `RESOLVED` alert never absorbs new events, so the next breach after resolution opens a fresh alert.

### 6.4 Alert JSON shape

`GET /api/soc/alerts/{id}` returns exactly the spec's §3.4 fields, with `relatedEvents` as an array of event IDs:

```json
{
  "alertId": "…",
  "timestamp": "2026-10-07T11:10:00Z",
  "ruleName": "Multiple Failed Logins",
  "severity": "HIGH",
  "status": "OPEN",
  "description": "5 failed login attempts for wickusvn@gmail.com within 5 minutes.",
  "relatedEvents": ["…", "…", "…", "…", "…"],
  "affectedService": "identity-service",
  "affectedUser": "wickusvn@gmail.com",
  "affectedEntity": "/api/auth/login",
  "recommendedAction": "Temporarily block the source IP and verify whether the account is compromised; consider forcing a password reset."
}
```

## 7. APIs

| Endpoint | Access | Purpose |
|---|---|---|
| `POST /api/events` | Internal only (not routed by the gateway); requires `X-Soc-Ingest-Key` | Batch ingestion. Validates each event individually; stores valid ones; returns `{ accepted, rejected }` counts. |
| `GET /api/soc/events?type=&user=&from=&to=` | Admin via gateway | Inspect collected events, newest first, paged. |
| `GET /api/soc/alerts?status=&severity=` | Admin via gateway | List alerts. |
| `GET /api/soc/alerts/{id}` | Admin via gateway | One alert in the §6.4 shape. |
| `PATCH /api/soc/alerts/{id}/status` | Admin via gateway | Body `{ "status": "ACKNOWLEDGED" \| "RESOLVED" }`. |
| `GET /api/soc/rules` | Admin via gateway | Rule documentation (§11.4) plus current thresholds. |

A rejected event in a batch (missing `eventId`, `eventType`, `serviceName` or `timestamp`, or invalid severity) is logged by SocService; the rest of the batch is still stored. A wrong or missing ingest key returns 401 for the whole batch.

## 8. Admin role and access control

- IdentityService adds `Admin` to the existing `Player` / `Coach` roles. Registration can never create an Admin.
- On startup, IdentityService ensures one admin account exists, from `Seed__AdminEmail` / `Seed__AdminPassword` environment variables in `docker-compose.yml` (default `admin@potchmatch.local`). Existing admin → no change.
- The gateway routes `/api/soc/{everything}` to `soc-service` with JWT authentication **and** `RouteClaimsRequirement` for the role claim `Admin`. A non-admin gets 403, which produces an `UNAUTHORISED_ACCESS` event — so probing the SOC APIs is itself detectable by Rule 2.
- SocService also validates the JWT and Admin role itself on all `/api/soc/*` endpoints (defence in depth, matching how the other services validate tokens). `POST /api/events` is the one exception: it uses the ingest key instead of a JWT, since the senders are services, not users.

## 9. Docker Compose changes

- New `soc-db` (Postgres 16, own volume) and `soc-service` services; `soc-service` depends on `consul` and `soc-db` being healthy.
- New environment variables: `SocIngest__Key` (shared by SocService and the four senders), `Seed__AdminEmail`, `Seed__AdminPassword`.
- Compose project `name` changes from `squash-platform` to `squash-platform-phase2`, so the Phase 2 stack gets its own containers and volumes and never collides with the untouched Phase 1 stack.

## 10. Error handling summary

| Situation | Behaviour |
|---|---|
| SocService down or restarting | Sinks buffer to disk and retry; events arrive late with `received_at` showing the delay. |
| Same batch delivered twice | Duplicate `event_id`s ignored. |
| Malformed event in a batch | That event rejected and logged; others stored. |
| A rule throws | Logged; other rules still evaluated this cycle. |
| Consul unreachable | Logged; no health events recorded (no false SERVICE_FAILURE). |
| Ingest key missing/wrong | 401, batch not stored. |

## 11. Testing

- **`SocService.Tests`** (xUnit), written test-first:
  - each rule: below threshold (no match), at threshold (match), events outside the window ignored, both grouping keys for Rules 1 and 2, the immediate-fire path and the count path for Rule 4;
  - alert writer: new alert created; open alert absorbs new events without duplicating links; acknowledged alert also absorbs; resolved alert does not (a new one is created); Rule 4 auto-resolve;
  - ingestion: valid batch stored; duplicate batch stored once; mixed batch stores valid events and reports rejected count; missing key → 401.
  - Persistence tests run against the EF Core in-memory provider; rules are pure functions over event lists and need no database.
- **End-to-end check** — new Postman folder "Phase 2 — SOC":
  1. Login as admin (captures admin token).
  2. Five bad logins for one account.
  3. Wait ≥15 s, then `GET /api/soc/alerts?status=OPEN` → one HIGH "Multiple Failed Logins" alert.
  4. `GET /api/soc/alerts/{id}` → `relatedEvents` contains five event IDs, each retrievable via `/api/soc/events`.
  5. A player token calling `/api/soc/alerts` → 403.
- **Rule 3:** a short PowerShell loop sending >100 requests in a minute (or a lowered demo threshold).
- **Rule 4:** `docker compose stop payment-service` → SERVICE_FAILURE alert within ~15 s; `docker compose start payment-service` → alert auto-resolved.

## 12. Handover to Cycle B

Cycle B will read events and alerts only through SocService's API (`/api/soc/events`, `/api/soc/alerts/{id}` with `relatedEvents`). Nothing graph-specific is added to `soc_db` in Cycle A.
