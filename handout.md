# PotchMatch — Project Handout

**Project name:** PotchMatch: Microservice Athlete Tournament & Coaching Hub (MATCH)
**Module:** ITRI623
**Status:** Deliverable 1 (microservices application) complete and submitted. Deliverable 2 (Knowledge Graph / RAG / SOC layer) not yet started.
**Prepared:** as a handover reference for continuing into Phase 2.

---

## 1. What the project is

Squash is a growing competitive sport in South Africa and is set to become an Olympic sport by 2028. PotchMatch exists to support professional players and coaches financially by providing a platform for fee-based challenge matchmaking, where coaches can also advertise paid coaching sessions — with the long-term goal of growing the sport in South Africa. It currently models squash clubs across the North-West province (Potchefstroom, Klerksdorp, Rustenburg, Coligny, Fochville, and Vaal Reefs), with room to expand nationally later.

Functionally, a player or coach can:

- register and log in (separate first-name/surname fields, JWT-based session);
- edit their own profile — name, surname, email, club, and, if they're a coach, a bio, coaching level, and hourly rate;
- change their password;
- host a challenge (any player) or a coaching session (coaches only), each with a fee in ZAR and a scheduled date;
- browse clubs on a map and see open challenges/coaching sessions at each one;
- accept an open challenge or session, which places the fee in escrow and, on success, pays it out (minus a 10% platform commission) to the host, along with the host's contact email so the two parties can arrange a time;
- top up an in-app wallet through a demo "Add Funds" flow (no real card processing).

## 2. Architecture

A single Angular 22 frontend talks to everything through one API Gateway; nothing else is reachable directly from outside the Docker network except the gateway, the databases (for local inspection via `psql`/pgAdmin), and Consul's UI.

| Component | Technology | Role |
|---|---|---|
| Frontend | Angular 22 (signals, no routing — one component tree, view switched via signals) | UI, served in production by its own nginx container after a multi-stage build |
| API Gateway | Ocelot (.NET 10) | Single entry point on `:5132`; routes `/api/auth`, `/api/players`, `/api/bookings`, `/api/payments` to the right service; enforces JWT auth on protected routes; resolves downstream addresses dynamically through Consul (no hardcoded host/port anywhere in `ocelot.json`) |
| Service registry | Consul (dev-mode agent) | IdentityService, BookingService and PaymentService self-register on startup and are health-checked every 10s; the Gateway asks Consul for the live address at request time |
| IdentityService | ASP.NET Core Web API + EF Core + PostgreSQL (`identity_db`) | Registration, login, JWT issuance, player profile CRUD, password change |
| BookingService | ASP.NET Core Web API + EF Core + PostgreSQL (`booking_db`) | Challenges and coaching sessions; hosts, lists, and accepts bookings; orchestrates the payment saga (see §4) |
| PaymentService | ASP.NET Core Web API + EF Core + PostgreSQL (`payment_db`) | Wallets, deposits, and escrow transactions (hold / release / refund) |
| Logging | Serilog (compact JSON to stdout) | Every service, including the Gateway, logs structured JSON with a `ServiceName` property and a `CorrelationId` that follows one request across every service it touches |
| Orchestration | Docker Compose | One `docker-compose.yml` (in `backend/`) builds and runs all of the above plus the frontend — a single `docker compose up -d` (after one `--build`) brings up the whole system |

Full diagram: `architecture-diagram.png` (submitted with Deliverable 1).

### Database-per-service

`identity_db`, `booking_db`, and `payment_db` are three separate PostgreSQL containers with separate volumes. No service holds a connection string to another service's database, and no `DbContext` references another service's tables — e.g. BookingService knows a `HostPlayerId` (a `Guid`) but has no way to query player details except by calling IdentityService's API. Each service defines its own copy of any shared-shaped DTO it needs (e.g. `HoldFundsRequest` exists almost identically in both BookingService and PaymentService) rather than sharing a library — a deliberate trade-off of a little duplication for services that never need to be built or deployed together.

## 3. API surface

Base URL: `http://localhost:5132`. 🔒 = requires a JWT bearer token.

**IdentityService** (`/api/auth`, `/api/players`)
- `POST /api/auth/register` — create a player or coach account
- `POST /api/auth/login` — authenticate, returns a JWT
- `GET 🔒 /api/players` — list all players
- `GET 🔒 /api/players/{id}` — get one player's profile
- `PUT 🔒 /api/players/{id}` — update a player's own profile
- `PUT 🔒 /api/players/{id}/password` — change a player's own password

**BookingService** (`/api/bookings`)
- `GET 🔒 /api/bookings?clubId={id}` — list bookings, optionally by club
- `GET 🔒 /api/bookings/{id}` — get one booking
- `POST 🔒 /api/bookings` — host a challenge or coaching session
- `PUT 🔒 /api/bookings/{id}/accept` — accept an open booking (triggers the payment saga)

**PaymentService** (`/api/payments`)
- `GET 🔒 /api/payments/{id}` — get one escrow transaction
- `GET 🔒 /api/payments/by-booking/{bookingId}` — get the escrow transaction for a booking
- `GET 🔒 /api/payments/wallet` — get the caller's own wallet balance
- `POST 🔒 /api/payments/wallet/deposit` — top up the caller's own wallet
- `POST 🔒 /api/payments/hold` — hold funds in escrow (called by BookingService, not the frontend)
- `PUT 🔒 /api/payments/{id}/release` — release held funds to the payee
- `PUT 🔒 /api/payments/{id}/refund` — refund held funds to the payer

Full request/response examples are in `deliverable-1-content.md`, and a ready-to-run Postman collection (`postman/PotchMatch-Demo.postman_collection.json` + environment) exercises the whole flow end to end.

## 4. Microservices patterns implemented

The brief required at least two; three were implemented:

**1. Database per Service** — see §2 above.

**2. Circuit Breaker + Retry** (Polly, in `BookingService/Program.cs`) — the call from BookingService to PaymentService (`POST /api/payments/hold`) goes through a resilience pipeline: up to 3 retries with exponential backoff and a 5-second per-attempt timeout for transient failures, and a circuit breaker that opens for 15 seconds once over 50% of calls fail within a 10-second window (minimum 3 calls), after which calls fail fast with a clean `503` instead of hammering a struggling PaymentService. The `ShouldHandle` predicate deliberately excludes legitimate 4xx business responses (like `402 Insufficient Funds`) from counting as failures — retrying a deterministic rejection would be pointless and would falsely trip the breaker (this was a real bug found and fixed during development; see §6).

**3. Saga pattern** (orchestration-based, with compensating transaction) — accepting a booking touches two services' databases with no shared transaction possible between them, so `BookingController.Accept()` runs an explicit saga with BookingService as the orchestrator: (1) hold the fee in escrow via PaymentService, (2) attempt BookingService's own atomic conditional update (`WHERE Status = Open`, which also doubles as the concurrency guard against two players accepting at once), then either (3a) call `/release` if the update succeeded, or (3b) call `/refund` — the compensating step — if it lost the race. Every step emits its own structured event (`ESCROW_HELD`, `BOOKING_ACCEPTED`, `BOOKING_ACCEPT_LOST_RACE`, `ESCROW_RELEASED`/`ESCROW_REFUNDED`).

## 5. Security event logging (built for Phase 2 from day one)

Every service (including the Gateway) emits structured events through a shared `SecurityEventLog.Emit(...)` helper (one identical copy per service — `Logging/SecurityEventLog.cs`), deliberately shaped to match the Phase 2 SOC event schema:

```
eventType, severity, message, userId, sourceIp, endpoint, httpMethod, statusCode, affectedEntity
```

`ServiceName` is attached automatically to every log line via a Serilog enricher, and `CorrelationId` is pushed into Serilog's `LogContext` by a small piece of gateway/service middleware that reads (or mints) an `X-Correlation-Id` header — so every log line from every service touched by one request shares the same id, without needing to pass it manually. `timestamp` is Serilog's own timestamp on every event, automatically.

Event types currently emitted:

| Category | Event types |
|---|---|
| Authentication | `LOGIN_SUCCESS`, `FAILED_LOGIN` |
| Authorisation | `AUTH_TOKEN_REJECTED`, `UNAUTHORISED_ACCESS` |
| Suspicious activity | `SUSPICIOUS_ACTIVITY` (e.g. a player trying to accept their own booking) |
| Business events | `PLAYER_REGISTERED`, `BOOKING_CREATED`, `BOOKING_ACCEPTED`, `BOOKING_ACCEPT_LOST_RACE`, `BOOKING_ACCEPT_BLOCKED_INSUFFICIENT_FUNDS`, `WALLET_DEPOSIT`, `ESCROW_HELD`, `ESCROW_RELEASED`, `ESCROW_REFUNDED`, `DUPLICATE_ESCROW_HOLD_ATTEMPT`, `ESCROW_HOLD_BLOCKED_INSUFFICIENT_FUNDS` |
| Inter-service reliability | `INTER_SERVICE_CALL`, `INTER_SERVICE_CALL_FAILED` (covers both the circuit breaker opening and a downstream 5xx) |
| Unhandled errors | every unhandled exception logged with full message and stack context |

All of this ships out as JSON to stdout / `docker compose logs <service>` today — there is no SOC storage/collection layer yet (see §8).

## 6. Bugs found and fixed during development

Worth knowing about if similar patterns get reused in Phase 2 code:

- **EF Core migrations silently skipped.** BookingService's and PaymentService's migrations were missing their `.Designer.cs` files (the `[Migration("...")]` attribute EF Core needs to discover a migration). Without it, EF Core logs only a quiet warning and excludes the migration entirely — no exception, no table. Root cause of an early "relation Bookings does not exist" error. Fixed by writing the missing Designer files by hand; from that point on, every new migration in this project was created with its Designer file from the start.
- **Circuit breaker tripping on legitimate business rejections.** Polly's `ShouldHandle` predicate originally treated any non-2xx response (including the deliberate `402 Insufficient Funds`) as a failure, so repeated legitimate rejections opened the circuit and masked the real answer behind a generic `503`. Fixed by scoping `ShouldHandle` to actual infrastructure failures only (5xx, timeouts, connection errors).
- **Consul's advertised address unreachable.** With no `-node`/`-advertise` flags, Consul's dev-mode agent identified itself by its auto-assigned container hostname, which nothing else could resolve — the Gateway ended up trying to call Consul's own container instead of the real service. Fixed by pinning `-node=consul -advertise={{ GetInterfaceIP "eth0" }}`.
- **Ocelot's default Consul integration prefers the wrong address.** Ocelot's `DefaultConsulServiceBuilder` prefers a Consul catalog node's *name* over the service's own registered *address* — here that resolved every downstream call to `consul:8080` (Consul's own management API) instead of the real service. Fixed with a custom `AddressPreferringConsulServiceBuilder` overriding `GetDownstreamHost` to always use `entry.Service.Address` (see `ApiGateway/AddressPreferringConsulServiceBuilder.cs` for the full writeup, with a link to the relevant line in Ocelot's own source).

## 7. Known limitations / deliberate simplifications

These were conscious scope decisions for Phase 1, not oversights — worth stating plainly rather than discovering later:

- **No real payment processing.** The "Add Funds" flow is a demo top-up — no card details are sent or validated anywhere; it exists purely to demonstrate the escrow → wallet flow end to end.
- **Release/refund have no caller check.** Any authenticated user can call `PUT /api/payments/{id}/release` or `/refund` for any transaction id — noted directly in the code as a real-system gap ("only an admin, or both players confirming, should be able to trigger this").
- **`rank` isn't editable.** It's a placeholder for a future external ranking integration (referred to in code comments as "the mocked SportyHQ rank") — the profile update endpoint accepts it but the UI just echoes back whatever the player already has.
- **Clubs aren't a backend entity.** The list of clubs (with their `clubId` codes like `pcc`, `defence`, etc.) lives only in the Angular frontend's mock data, not in any service's database — `clubId` is just an opaque string tag on players and bookings.
- **Application Metrics (spec §6.3) not implemented** — a deliberate decision after confirming with the Phase 2 spec that metrics aren't part of that phase's continuity (which uses the event/log schema in §5 instead), so this was left out of Phase 1 as out of scope rather than an oversight.
- **`eventId` is not yet a distinct field.** The SOC event shape in §5 omits a unique `eventId` per event (Serilog gives each line an implicit position/timestamp, but nothing matching the spec's `"eventId": "evt-1021"` style identifier). This will need adding — trivially, a GUID per `Emit()` call — before Phase 2's event collection layer can reference individual events by id.
- **`sourceIp` is defined but never populated.** The parameter exists on every `SecurityEventLog.Emit()` call, but no call site currently passes it. Wiring it up (`HttpContext.Connection.RemoteIpAddress`) is a small, contained change.

## 8. Readiness for Phase 2 (Knowledge Graph / RAG / SOC)

Mapped directly against the Phase 2 spec's required components:

| Phase 2 component | Status | Notes |
|---|---|---|
| §3.1 SOC Event Collection Layer | **Mostly ready** | Every service already emits shaped events (§5) from the Gateway, an auth service, and more than two domain services — satisfies the spec's minimum sources. Still needed: add `eventId`, populate `sourceIp`, and decide where these JSON log lines actually get *collected into* (see next row) |
| §3.2 SOC Storage/Logging Component | **Not started** | Logs currently only go to stdout/Docker logs. Needs either a tool like Better Stack, or a small custom ingestion service that tails/reads the structured logs into a queryable store |
| §3.3 Threat Detection Rules | **Not started** | No rules exist yet. The event catalogue in §5 already covers good raw material for at least three of the spec's example rules — `FAILED_LOGIN` → Rule 1 (multiple failed logins), `UNAUTHORISED_ACCESS`/`AUTH_TOKEN_REJECTED` → Rule 2 (unauthorised endpoint access), and `INTER_SERVICE_CALL_FAILED`/circuit-breaker-open → Rule 4 (service failure) — without needing new instrumentation, just detection logic reading the existing events |
| §3.4 Alert Service | **Not started** | No alert model or storage yet |
| §4 Knowledge Graph (Neo4j) | **Not started** | No graph database, schema, or data-loading yet. The application already produces natural candidates for `Service`, `Endpoint`, `User`, and `Event` nodes and a `Service-[:DEPENDS_ON]->Service` relationship (Gateway → each domain service; BookingService → PaymentService) straight from what exists today |
| §5 RAG over the Knowledge Graph | **Not started** | Depends on the graph existing first |

**Suggested first steps for Phase 2**, in a sensible order given what's already in place:

1. Add `eventId` (GUID) and wire up `sourceIp` in `SecurityEventLog.Emit()` across all four services — a small, mechanical change to code that already exists.
2. Stand up a place to actually collect the JSON log lines (Better Stack is the path of least resistance given they're already structured JSON to stdout; a custom ingestion service is more work but demonstrates more).
3. Write the first detection rule against `FAILED_LOGIN` events (the spec's own example rule), since IdentityService already emits exactly that event on every login attempt.
4. Stand up Neo4j and model `Service`/`Endpoint`/`DEPENDS_ON` directly from the architecture in §2 — this data doesn't depend on any Phase 2 code being written yet, so it can be loaded immediately.
5. Layer events → alerts → the rest of the required node types once the first rule and the graph both exist.

## 9. Deliverable 1 submission artifacts

- `Project_Deliverable_1/deliverable-1-content.md` — full write-up (functionality, API docs, architecture, patterns, security event logging), for pasting into the Word document.
- `Project_Deliverable_1/architecture-diagram.png` — architecture diagram (Graphviz).
- `Project_Deliverable_1/squash-platform-source-code.zip` — the submitted source code zip (source + deployment files only, no generated artifacts).
- Demo video (~9:50) — full walkthrough: registration, coach profile setup, hosting a coaching session, a deliberate `402 Insufficient Funds` failure, a live circuit-breaker trip against Consul, funding a wallet, and a successful accept, with code cutaways for the Saga pattern.
- `squash-platform/postman/` — the Postman collection, environment, and the full timed recording script used to produce the video, kept in the project for anyone picking this up later.

---

*This handout reflects the state of the project as of Deliverable 1's submission (6 September 2026). Everything in §8's "Not started" rows is exactly that — none of it exists in the codebase yet, so Phase 2 is starting from a clean slate on the graph/RAG side, but with a genuinely useful head start on the event data that side will consume.*
