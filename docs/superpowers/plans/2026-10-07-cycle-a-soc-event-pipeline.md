# Cycle A — SOC Event Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collect security/operational events from all PotchMatch services into a new SocService, run four detection rules over them, and produce deduplicated alerts linked to their triggering events, exposed through admin-only APIs.

**Architecture:** A new .NET 10 `SocService` with its own `soc_db` receives events pushed by a durable Serilog HTTP sink in each existing service, polls Consul for health transitions, and runs a 15-second detection loop that writes alerts. Admin read APIs are routed through the Ocelot gateway behind a new `role: Admin` claim requirement.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core 10 + Npgsql, Serilog + `Serilog.Sinks.Http` (9.x), Ocelot 24.1.0 + Consul, xUnit + EF Core InMemory, Docker Compose, Postman.

**Spec:** `docs/superpowers/specs/2026-10-07-cycle-a-soc-event-pipeline-design.md`

## Global Constraints

- All work is in `squash-platform-phase2`; the original `squash-platform` folder is never modified.
- Target framework `net10.0`; package versions match the existing services' (`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Microsoft.EntityFrameworkCore.Design` 10.0.11, `Serilog.AspNetCore` 10.0.0, `Consul` 1.8.0, `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11).
- Every EF Core migration must have its `.Designer.cs` with `[DbContext]` and `[Migration]` attributes (generate with `dotnet ef migrations add`; never hand-write a migration without one).
- Follow the existing per-service duplication convention (each service owns its own copy of small cross-cutting classes; no shared library).
- Severity values are exactly `LOW`, `MEDIUM`, `HIGH`. Alert status values are exactly `OPEN`, `ACKNOWLEDGED`, `RESOLVED`.
- Alert `ruleName` values are exactly: `Multiple Failed Logins`, `Unauthorised Endpoint Access`, `POSSIBLE_DOS`, `SERVICE_FAILURE`.
- Docker Compose project name becomes `squash-platform-phase2`. New env vars: `SocIngest__Key`, `Seed__AdminEmail` (default `admin@potchmatch.local`), `Seed__AdminPassword`.
- `dotnet` commands run wherever the .NET 10 SDK is available; `docker compose` commands run from `backend/` on the Windows machine.
- Commit messages end with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_019wuPNJ5g7LKK4idzgvacog
  ```

## Review Focus

1. **A manually resolved alert must not reopen from the same old events** still inside the rule's window on the next 15-second cycle → test in Task 4 (`Engine_DoesNotReopen_ResolvedAlert_FromSameEvents`).
2. **The same account typed with different case/whitespace** (`Wickus@Gmail.com ` vs `wickusvn@gmail.com`) must count as one account in Rule 1 → test in Task 5 (`FailedLogins_NormalisesEmailCase`).
3. **The same `eventId` twice inside one batch** (not just across retries) must be stored once without an exception → test in Task 2 (`Ingest_DuplicateWithinBatch_StoredOnce`).
4. **Events with neither `userId` nor `sourceIp`** must not be grouped together under an empty key in Rule 2 → test in Task 5 (`UnauthorisedAccess_IgnoresEventsWithNoUserOrIp`).
5. **A service that recovers and then fails again** must produce a new SERVICE_FAILURE alert after the first was auto-resolved → test in Task 6 (`ServiceFailure_FailsAgainAfterRecovery_NewMatch`).

---

## File Structure

**New — `backend/src/SocService/`**
- `SocService.csproj`, `Dockerfile`, `.dockerignore`, `appsettings.json`, `Program.cs` — host, Serilog, Consul registration, JWT, workers.
- `Data/SocDbContext.cs`, `Migrations/*` — `events`, `alerts`, `alert_events`.
- `Models/SocEvent.cs`, `Models/Alert.cs`, `Models/AlertEvent.cs`, `Models/SocConstants.cs` (severity/status/rule-name strings).
- `Ingestion/SerilogEnvelope.cs` — wire format posted by the HTTP sink.
- `Ingestion/SerilogEventMapper.cs` — envelope → `SocEvent` (+ validation).
- `Ingestion/EventIngestionService.cs` — idempotent batch store.
- `Ingestion/IngestKeyValidator.cs` — constant-time key check.
- `Detection/IDetectionRule.cs`, `Detection/RuleMatch.cs`, `Detection/DetectionOptions.cs`.
- `Detection/Rules/MultipleFailedLoginsRule.cs`, `UnauthorisedEndpointAccessRule.cs`, `AbnormalRequestRateRule.cs`, `ServiceFailureRule.cs`.
- `Detection/AlertWriter.cs` — dedup/absorb/create/resolve.
- `Detection/DetectionEngine.cs` — one evaluation pass; `Detection/DetectionWorker.cs` — 15 s `BackgroundService`.
- `Health/IConsulHealthSource.cs`, `Health/ConsulHealthSource.cs`, `Health/HealthTransitionDetector.cs`, `Health/HealthPollerWorker.cs`.
- `Controllers/EventIngestController.cs` (`POST /api/events`), `Controllers/SocEventsController.cs`, `Controllers/AlertsController.cs`, `Controllers/RulesController.cs`.
- `Dtos/AlertResponse.cs`, `Dtos/SocEventResponse.cs`, `Dtos/RuleDocResponse.cs`.

**New — `backend/tests/SocService.Tests/`** — xUnit project; one test file per unit above.

**Modified**
- `backend/src/{ApiGateway,IdentityService,BookingService,PaymentService}/…/SecurityEventLog.cs` — EventId + SecurityEvent marker.
- Same four services: `Program.cs` (sink, ClientIp push, forwarded headers), `*.csproj` (`Serilog.Sinks.Http`), new `Logging/SocIngestHttpClient.cs` (ApiGateway: project root, next to its `SecurityEventLog.cs`).
- `backend/src/ApiGateway/ocelot.json` — `X-Forwarded-For` transform on all routes; new `/api/soc/{everything}` route.
- `backend/src/ApiGateway/Program.cs` — `MapInboundClaims = false`; emit `UNAUTHORISED_ACCESS` on gateway 403.
- `backend/src/IdentityService/Services/AdminSeeder.cs` (new) + `Program.cs`.
- `backend/docker-compose.yml`, `backend/SquashPlatform.slnx`.
- `postman/PotchMatch-Demo.postman_collection.json` (new folder), `scripts/trigger-request-rate.ps1` (new).

---

### Task 1: SocService skeleton, data model, test project

**Files:**
- Create: `backend/src/SocService/{SocService.csproj,Dockerfile,.dockerignore,appsettings.json,Program.cs}`, `Data/SocDbContext.cs`, `Models/{SocEvent,Alert,AlertEvent,SocConstants}.cs`, `Migrations/*`
- Create: `backend/tests/SocService.Tests/{SocService.Tests.csproj,TestDb.cs,SocDbContextTests.cs}`
- Modify: `backend/SquashPlatform.slnx`, `backend/docker-compose.yml`

**Interfaces:**
- Produces:
  - `SocEvent { Guid EventId; DateTimeOffset Timestamp; DateTimeOffset ReceivedAt; string ServiceName; string EventType; string Severity; string? UserId; string? SourceIp; string? Endpoint; string? HttpMethod; int? StatusCode; string Message; string? CorrelationId; string? AffectedEntity; }`
  - `Alert { Guid AlertId; DateTimeOffset Timestamp; DateTimeOffset UpdatedAt; string RuleName; string Severity; string Status; string Description; string? AffectedService; string? AffectedUser; string? AffectedEntity; string RecommendedAction; string DedupKey; List<AlertEvent> AlertEvents; }`
  - `AlertEvent { Guid AlertId; Guid EventId; }` (composite PK)
  - `SocDbContext` with `DbSet<SocEvent> Events`, `DbSet<Alert> Alerts`, `DbSet<AlertEvent> AlertEvents`; snake_case table/column names per spec §6.1–6.2; indexes `(event_type, timestamp)`, `(source_ip, timestamp)`, `(user_id, timestamp)`, `(dedup_key, status)`.
  - `SocConstants.Severity.{Low,Medium,High}`, `SocConstants.Status.{Open,Acknowledged,Resolved}`, `SocConstants.Rules.{FailedLogins,UnauthorisedAccess,RequestRate,ServiceFailure}` holding the exact Global Constraints strings.
  - Test helper `TestDb.Create() -> SocDbContext` (InMemory, unique database name per call).

- [ ] **Step 1: Create the test project** referencing `SocService`, xUnit, `Microsoft.EntityFrameworkCore.InMemory` 10.x; add both projects to `SquashPlatform.slnx` (test project under a `/tests/` folder).

- [ ] **Step 2: Write the failing test** `SocDbContextTests.CanStoreAlertLinkedToEvents`: add one `SocEvent`, one `Alert` with `Status = "OPEN"` and one `AlertEvent` linking them; save; reload the alert with `Include(a => a.AlertEvents)` and assert `AlertEvents.Single().EventId == event.EventId`.

- [ ] **Step 3: Run** `dotnet test backend/tests/SocService.Tests` — Expected: FAIL (types don't exist).

- [ ] **Step 4: Implement** models, `SocDbContext` and a minimal `Program.cs` following `PaymentService/Program.cs`: Serilog to console with `ServiceName = "soc-service"`, `db.Database.Migrate()` on startup, health checks at `/health`, Consul registration with `Name = "soc-service"`, controllers. Dockerfile copied from PaymentService's pattern with names changed. Generate `Migrations/InitialCreate` with `dotnet ef migrations add InitialCreate` and confirm the `.Designer.cs` exists.

- [ ] **Step 5: Run tests** — Expected: PASS.

- [ ] **Step 6: Compose** — add `soc-db` (postgres:16, `POSTGRES_DB: soc_db`, host port `5436:5432`, own volume `soc-db-data`, healthcheck as other dbs) and `soc-service` (build `./src/SocService`, depends on healthy `consul` + `soc-db`, env `ConnectionStrings__SocDb`, `Consul__Address`, `ServiceRegistration__Address/HealthCheckHost: soc-service`, `SocIngest__Key`, Jwt settings as other services). Change compose `name:` to `squash-platform-phase2`.

- [ ] **Step 7: Verify** `docker compose up --build -d` then `docker compose ps` — Expected: `soc-service` running; Consul UI (`localhost:8500/ui`) lists `soc-service` passing; `docker compose exec soc-db psql -U postgres -d soc_db -c "\dt"` lists `events`, `alerts`, `alert_events`.

- [ ] **Step 8: Commit** — `feat(soc): SocService skeleton with soc_db data model`

---

### Task 2: Event ingestion endpoint

**Files:**
- Create: `Ingestion/{SerilogEnvelope,SerilogEventMapper,EventIngestionService,IngestKeyValidator}.cs`, `Controllers/EventIngestController.cs`
- Test: `backend/tests/SocService.Tests/{SerilogEventMapperTests,EventIngestionServiceTests,IngestKeyValidatorTests}.cs`

**Interfaces:**
- Consumes: `SocDbContext`, `SocEvent` (Task 1).
- Produces:
  - Wire format — `SerilogEnvelope { DateTimeOffset Timestamp; string? Level; string? RenderedMessage; Dictionary<string, JsonElement> Properties; }`. This is the shape `Serilog.Sinks.Http`'s default `NormalRenderedTextFormatter` posts; the request body is a JSON array of these.
  - `SerilogEventMapper.TryMap(SerilogEnvelope env, DateTimeOffset receivedAt, out SocEvent? evt, out string? error) -> bool`. Field sources (Properties keys): `EventId`, `EventType`, `Severity`, `ServiceName`, `UserId`, `SourceIp` (fallback `ClientIp` when `SourceIp` is null/empty), `Endpoint`, `HttpMethod`, `StatusCode`, `Message`, `CorrelationId`, `AffectedEntity`; `Timestamp` from the envelope. Rejects when `EventId` isn't a GUID, `EventType` or `ServiceName` missing, or `Severity` not LOW/MEDIUM/HIGH (case-insensitive input, stored upper-case).
  - `EventIngestionService.IngestAsync(IReadOnlyList<SerilogEnvelope> batch, CancellationToken ct) -> Task<IngestResult>`; `record IngestResult(int Accepted, int Rejected)`. Duplicates (within the batch or already in `soc_db`) count as accepted but are stored once.
  - `IngestKeyValidator.IsValid(string? provided, string expected) -> bool` (uses `CryptographicOperations.FixedTimeEquals`; false for null/empty).
  - `POST /api/events` — `[AllowAnonymous]`, reads header `X-Soc-Ingest-Key`, 401 when invalid, else 200 with `IngestResult`. Config key `SocIngest:Key`.

- [ ] **Step 1: Write failing tests**
  - `Mapper_MapsAllSpecFields` — envelope with every property → `SocEvent` fields equal input; `Severity == "HIGH"` when input `"high"`.
  - `Mapper_UsesClientIp_WhenSourceIpMissing` — `SourceIp` absent, `ClientIp = "10.0.0.5"` → `SourceIp == "10.0.0.5"`.
  - `Mapper_Rejects_InvalidEventId` / `_MissingEventType` / `_UnknownSeverity` — `TryMap` returns false with non-null `error`.
  - `Ingest_StoresValidBatch` — 3 valid → `IngestResult(3, 0)`, 3 rows.
  - `Ingest_SameBatchTwice_StoredOnce` — ingest same 3 twice → 3 rows.
  - `Ingest_DuplicateWithinBatch_StoredOnce` — batch with one envelope repeated → 1 row, no exception.
  - `Ingest_MixedBatch_StoresValid_CountsRejected` — 2 valid + 1 invalid → `(2, 1)`, 2 rows.
  - `KeyValidator_RejectsNullEmptyAndWrong`, `KeyValidator_AcceptsExact`.

- [ ] **Step 2: Run** `dotnet test backend/tests/SocService.Tests` — Expected: FAIL.

- [ ] **Step 3: Implement** the four classes and the controller. Rejected events are logged with Serilog (not as security events). Register `EventIngestionService` as scoped.

- [ ] **Step 4: Run tests** — Expected: PASS.

- [ ] **Step 5: Commit** — `feat(soc): idempotent event ingestion endpoint`

---

### Task 3: Ship events from the four services

**Files:**
- Modify: `SecurityEventLog.cs` ×4, `Program.cs` ×4, `*.csproj` ×4, `ApiGateway/ocelot.json`
- Create: `SocIngestHttpClient.cs` ×4 (`Logging/` folder; ApiGateway at project root)

**Interfaces:**
- Consumes: `POST /api/events` + `X-Soc-Ingest-Key` (Task 2).
- Note: the spec says call sites "that currently omit sourceIp are updated". This plan meets the same requirement without touching ~30 call sites: every request's log context carries `ClientIp`, and the mapper (Task 2) uses it whenever `SourceIp` is absent.
- Produces: every `SecurityEventLog.Emit()` log event carries properties `EventId` (new GUID string) and `SecurityEvent = true` (pushed with `LogContext.PushProperty` around the existing `logger.Log` call — the `Emit` signature does not change). Every request's log context carries `ClientIp`.

- [ ] **Step 1: `SecurityEventLog.Emit`** (all four copies): wrap the existing `logger.Log(...)` in `using (LogContext.PushProperty("EventId", Guid.NewGuid().ToString())) using (LogContext.PushProperty("SecurityEvent", true))`.

- [ ] **Step 2: Real client IP.**
  - `ocelot.json`: add `"UpstreamHeaderTransform": { "X-Forwarded-For": "{RemoteIpAddress}" }` to every route.
  - Identity/Booking/Payment `Program.cs`: `builder.Services.Configure<ForwardedHeadersOptions>` with `ForwardedHeaders = XForwardedFor`, `KnownIPNetworks` and `KnownProxies` cleared (the gateway's Docker IP isn't fixed); `app.UseForwardedHeaders()` as the first middleware.
  - All four: in the existing correlation-id middleware, also `LogContext.PushProperty("ClientIp", context.Connection.RemoteIpAddress?.ToString())`.

- [ ] **Step 3: Sink.** Add `Serilog.Sinks.Http` 9.x to the four csproj files. `SocIngestHttpClient : Serilog.Sinks.Http.IHttpClient` wraps an `HttpClient` and adds header `X-Soc-Ingest-Key` from `SocIngest:Key` (confirm the interface's `Configure`/`PostAsync` signatures against the installed package). In each `UseSerilog` configuration add:
  ```csharp
  .WriteTo.Logger(lc => lc
      .Filter.ByIncludingOnly(Matching.WithProperty("SecurityEvent"))
      .WriteTo.DurableHttpUsingFileSizeRolledBuffers(
          requestUri: context.Configuration["SocIngest:Url"] ?? "http://soc-service:8080/api/events",
          bufferBaseFileName: "soc-buffer/events",
          httpClient: new SocIngestHttpClient(context.Configuration)))
  ```
  Compose: add `SocIngest__Url` and `SocIngest__Key` to the four services.

- [ ] **Step 4: Verify** — `docker compose up --build -d`; attempt one bad login in Postman; then `docker compose exec soc-db psql -U postgres -d soc_db -c "select event_type, service_name, user_id, source_ip from events order by timestamp desc limit 5;"` — Expected: a `FAILED_LOGIN` row from `identity-service` with the attempted email and a `source_ip` that is **not** the gateway container's IP (it should match the gateway's own `REQUEST_RECEIVED` row for the same request); `REQUEST_RECEIVED` rows from `api-gateway`.

- [ ] **Step 5: Verify durability** — `docker compose stop soc-service`, do one bad login, `docker compose start soc-service`, wait 30 s, re-run the query — Expected: the login made while SocService was down is present.

- [ ] **Step 6: Commit** — `feat: ship security events from all services to SocService`

---

### Task 4: Detection framework, alert writer and engine

**Files:**
- Create: `Detection/{IDetectionRule,RuleMatch,DetectionOptions,AlertWriter,DetectionEngine,DetectionWorker}.cs`, `appsettings.json` section
- Test: `AlertWriterTests.cs`, `DetectionEngineTests.cs`

**Interfaces:**
- Consumes: `SocDbContext`, models, `SocConstants` (Task 1).
- Produces:
  ```csharp
  public interface IDetectionRule
  {
      string RuleName { get; }            // exact Global Constraints value
      string Purpose { get; }
      IReadOnlyList<string> InputEventTypes { get; }
      string DetectionCondition { get; }  // human-readable, includes current thresholds
      string Severity { get; }
      string AlertGenerated { get; }
      string RecommendedResponse { get; }
      TimeSpan Window { get; }
      IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now);
  }
  public record RuleMatch(string DedupKey, string Description, string? AffectedService,
      string? AffectedUser, string? AffectedEntity, IReadOnlyList<Guid> EventIds);
  ```
  - `DetectionOptions` bound from section `DetectionRules`: `FailedLogins { Threshold = 5, WindowMinutes = 5 }`, `UnauthorisedAccess { Threshold = 3, WindowMinutes = 5 }`, `RequestRate { Threshold = 100, WindowSeconds = 60 }`, `ServiceFailure { Threshold = 3, WindowMinutes = 2 }`, `EngineIntervalSeconds = 15`.
  - `AlertWriter.ApplyAsync(IDetectionRule rule, RuleMatch match, DateTimeOffset now, CancellationToken ct) -> Task<Alert>` — spec §6.3: absorb into the alert with the same `DedupKey` in `OPEN`/`ACKNOWLEDGED` (add only missing links, set `UpdatedAt`), else create `OPEN` alert with rule's `Severity`, `RecommendedResponse` as `RecommendedAction`.
  - `AlertWriter.ResolveAsync(string dedupKey, DateTimeOffset createdBefore, CancellationToken ct) -> Task<int>` — sets `RESOLVED` on `OPEN`/`ACKNOWLEDGED` alerts with that key and `Timestamp < createdBefore`; returns count.
  - `DetectionEngine.RunOnceAsync(DateTimeOffset now, CancellationToken ct) -> Task` — for each rule: load events with `EventType ∈ InputEventTypes` and `Timestamp >= now - Window`, **excluding events already linked to a `RESOLVED` alert of the same `RuleName`**; call `Evaluate`; `ApplyAsync` each match. A throwing rule is logged and skipped. Then calls `ServiceFailureRule.FindRecoveries` (Task 6) when that rule is registered. Rules are resolved from DI as `IEnumerable<IDetectionRule>`.
  - `DetectionWorker : BackgroundService` — calls `RunOnceAsync(DateTimeOffset.UtcNow)` every `EngineIntervalSeconds` in a new DI scope.

- [ ] **Step 1: Write failing tests** (use a test-only `FakeRule` returning preset matches):
  - `Writer_CreatesOpenAlert_WithLinkedEvents`
  - `Writer_OpenAlert_AbsorbsNewEvents_NoDuplicateLinks` — apply match {e1,e2} then {e2,e3} → one alert, links {e1,e2,e3}.
  - `Writer_AcknowledgedAlert_AlsoAbsorbs`
  - `Writer_ResolvedAlert_DoesNotAbsorb_NewAlertCreated`
  - `Writer_Resolve_OnlyResolvesOlderAlerts`
  - `Engine_DoesNotReopen_ResolvedAlert_FromSameEvents` — run engine (alert created), resolve it, run engine again with same events → still exactly one alert, status `RESOLVED`.
  - `Engine_FailingRule_DoesNotStopOtherRules` — throwing rule + FakeRule → FakeRule's alert still created.
  - `Engine_OnlyPassesEventsInsideWindow` — event older than `Window` not passed to `Evaluate`.

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** the interfaces above; register `AlertWriter`, `DetectionEngine` scoped and `DetectionWorker` hosted in `Program.cs`; add the `DetectionRules` section with the defaults to `appsettings.json`.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(soc): detection engine and deduplicating alert writer`

---

### Task 5: Rules 1 and 2

**Files:**
- Create: `Detection/Rules/MultipleFailedLoginsRule.cs`, `Detection/Rules/UnauthorisedEndpointAccessRule.cs`
- Test: `MultipleFailedLoginsRuleTests.cs`, `UnauthorisedEndpointAccessRuleTests.cs`

**Interfaces:**
- Consumes: `IDetectionRule`, `RuleMatch`, `DetectionOptions` (Task 4).
- Produces: two `IDetectionRule` implementations registered in DI. Documentation strings (`Purpose`, `DetectionCondition`, `AlertGenerated`, `RecommendedResponse`) taken from spec §5; responses verbatim:
  - Rule 1: "Temporarily block the source IP and verify whether the account is compromised; consider forcing a password reset."
  - Rule 2: "Review the user's role assignments and recent activity; revoke the session if access attempts continue."

**Rule 1 (`Multiple Failed Logins`, HIGH, input `FAILED_LOGIN`):** count events with `Timestamp >= now - window` per account key (`UserId` trimmed + lower-cased; skip null/empty) and separately per `SourceIp` (skip null/empty). Each group with count ≥ Threshold → match with `DedupKey` `Multiple Failed Logins:account:{email}` or `Multiple Failed Logins:ip:{ip}`; `AffectedService = "identity-service"`; `AffectedEntity = "/api/auth/login"`; `AffectedUser` = email (account group) or null (ip group); description `"{n} failed login attempts for {email|IP ip} within {w} minutes."`.

**Rule 2 (`Unauthorised Endpoint Access`, MEDIUM, input `AUTH_TOKEN_REJECTED`, `UNAUTHORISED_ACCESS`):** group by `UserId` when present, else by `SourceIp`; skip events with neither. Count ≥ Threshold → `DedupKey` `Unauthorised Endpoint Access:user:{id}` / `…:ip:{ip}`; `AffectedService` = most frequent `ServiceName` in the group; `AffectedEntity` = `Endpoint` of the latest event; description `"{n} rejected requests to protected endpoints by {user|IP} within {w} minutes."`.

- [ ] **Step 1: Write failing tests** (thresholds from default `DetectionOptions`):
  - `FailedLogins_FourAttempts_NoMatch`; `FailedLogins_FiveAttempts_SameAccount_Match` (EventIds has 5, DedupKey `Multiple Failed Logins:account:wickusvn@gmail.com`).
  - `FailedLogins_FiveAccounts_SameIp_IpMatch` — 5 different emails, one IP → exactly one match with `:ip:` key.
  - `FailedLogins_EventsOutsideWindow_Ignored` — 5 events, one 6 minutes old → no match.
  - `FailedLogins_NormalisesEmailCase` — 5 events with `UserId` values `"Wickusvn@Gmail.com "`, `"wickusvn@gmail.com"`, `"WICKUSVN@GMAIL.COM"`, `"wickusvn@gmail.com"`, `" wickusvn@gmail.com"` (no `SourceIp`) → exactly one match, key `Multiple Failed Logins:account:wickusvn@gmail.com`.
  - `UnauthorisedAccess_ThreeForSameUser_Match`; `UnauthorisedAccess_TwoForSameUser_NoMatch`.
  - `UnauthorisedAccess_NoUser_GroupsByIp` — 3 `AUTH_TOKEN_REJECTED` with null user, same IP → `:ip:` match.
  - `UnauthorisedAccess_IgnoresEventsWithNoUserOrIp` — 5 events with neither → no match.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** both rules and register them in `Program.cs`.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(soc): failed-login and unauthorised-access rules`

---

### Task 6: Rules 3 and 4 with auto-resolve

**Files:**
- Create: `Detection/Rules/AbnormalRequestRateRule.cs`, `Detection/Rules/ServiceFailureRule.cs`; modify `DetectionEngine.cs`
- Test: `AbnormalRequestRateRuleTests.cs`, `ServiceFailureRuleTests.cs`, add to `DetectionEngineTests.cs`

**Interfaces:**
- Consumes: Task 4 types; `AlertWriter.ResolveAsync`.
- Produces:
  - `AbnormalRequestRateRule` (`POSSIBLE_DOS`, HIGH, input `REQUEST_RECEIVED`): only events with `ServiceName == "api-gateway"`; per `SourceIp`, count **> Threshold** within `WindowSeconds` → `DedupKey` `POSSIBLE_DOS:ip:{ip}`, `AffectedService = "api-gateway"`, `AffectedEntity = "api-gateway"`. Response: "Rate-limit or temporarily block the source IP at the gateway."
  - `ServiceFailureRule` (`SERVICE_FAILURE`, HIGH, input `SERVICE_HEALTH_CRITICAL`, `SERVICE_HEALTH_RECOVERED`, `INTER_SERVICE_CALL_FAILED`, `UNHANDLED_EXCEPTION`). Affected service: `AffectedEntity` for health and inter-service events, `ServiceName` for `UNHANDLED_EXCEPTION`. Per service, let `R` = timestamp of the latest `SERVICE_HEALTH_RECOVERED` (or none). Match when there is a `SERVICE_HEALTH_CRITICAL` newer than `R`, **or** ≥ Threshold failures/exceptions newer than `R` within the window. EventIds = those critical/failure events (never the recovered ones). `DedupKey` `SERVICE_FAILURE:{service}`, `AffectedEntity` = service. Response: "Check the affected service's container and logs; dependent services are degraded until it recovers."
  - `ServiceFailureRule.FindRecoveries(IReadOnlyList<SocEvent> events) -> IEnumerable<(string Service, DateTimeOffset At)>` — latest recovered timestamp per service. The engine calls `AlertWriter.ResolveAsync($"SERVICE_FAILURE:{service}", at, ct)` for each.

- [ ] **Step 1: Write failing tests:**
  - `RequestRate_100_NoMatch`; `RequestRate_101_SameIp_Match`; `RequestRate_IgnoresNonGatewayEvents` (101 from `identity-service` → no match).
  - `ServiceFailure_HealthCritical_MatchesImmediately` (1 event → match for `payment-service`).
  - `ServiceFailure_TwoCallFailures_NoMatch`; `ServiceFailure_ThreeCallFailures_Match`.
  - `ServiceFailure_UnhandledException_UsesOwnServiceName`.
  - `ServiceFailure_CriticalThenRecovered_NoMatch`.
  - `ServiceFailure_FailsAgainAfterRecovery_NewMatch` — critical, recovered, critical → match containing only the last critical.
  - `Engine_RecoveredEvent_ResolvesServiceFailureAlert` — engine run with critical (alert OPEN), add recovered, run again → alert `RESOLVED`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** both rules, the recovery hook in `DetectionEngine.RunOnceAsync`, register in DI.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(soc): request-rate and service-failure rules with auto-resolve`

---

### Task 7: Consul health poller

**Files:**
- Create: `Health/{IConsulHealthSource,ConsulHealthSource,HealthTransitionDetector,HealthPollerWorker}.cs`
- Test: `HealthTransitionDetectorTests.cs`, `HealthPollerWorkerTests.cs`

**Interfaces:**
- Consumes: `SocDbContext`, `SocEvent`.
- Produces:
  - `IConsulHealthSource.GetStatusesAsync(CancellationToken ct) -> Task<IReadOnlyDictionary<string, string>>` — service name → `"passing"` / `"warning"` / `"critical"`; worst status wins across a service's checks; checks with empty `ServiceName` (Consul's `serfHealth`) skipped. `ConsulHealthSource` implements it with `IConsulClient.Health.State(HealthStatus.Any)`.
  - `HealthTransitionDetector.Detect(IReadOnlyDictionary<string,string>? previous, IReadOnlyDictionary<string,string> current, DateTimeOffset now) -> IReadOnlyList<SocEvent>` — `previous == null` (first poll) → empty. Non-critical → critical: `SERVICE_HEALTH_CRITICAL`, `HIGH`, message `"{service} health check is critical"`. Critical → non-critical: `SERVICE_HEALTH_RECOVERED`, `LOW`. Both: new `EventId`, `ServiceName = "soc-service"`, `AffectedEntity = service`, `Timestamp = ReceivedAt = now`. A service disappearing from `current` that was passing → treated as critical.
  - `HealthPollerWorker.PollOnceAsync(CancellationToken ct) -> Task` — gets statuses, detects, stores events, keeps `current` as `previous`. If the source throws, logs and returns **without** changing `previous` or storing anything. `ExecuteAsync` loops every 5 s.

- [ ] **Step 1: Write failing tests:**
  - `Detector_FirstPoll_NoEvents`; `Detector_PassingToCritical_CriticalEvent`; `Detector_CriticalToPassing_RecoveredEvent`; `Detector_NoChange_NoEvents`; `Detector_ServiceDisappears_CriticalEvent`.
  - `Poller_ConsulUnreachable_StoresNoEvents` — fake source throws on second poll after a first successful poll → 0 events stored, and a third poll with an unchanged status still produces no event.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** and register (`IConsulClient` singleton as in other services, `HealthPollerWorker` hosted).
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Verify** — `docker compose up --build -d soc-service`; `docker compose stop payment-service`; within ~30 s a `SERVICE_HEALTH_CRITICAL` row exists in `soc_db.events` and an `OPEN` `SERVICE_FAILURE` alert in `alerts`; `docker compose start payment-service` → `SERVICE_HEALTH_RECOVERED` row and the alert `RESOLVED`.
- [ ] **Step 6: Commit** — `feat(soc): Consul health poller emitting health transition events`

---

### Task 8: Admin read APIs

**Files:**
- Create: `Controllers/{SocEventsController,AlertsController,RulesController}.cs`, `Dtos/{AlertResponse,SocEventResponse,RuleDocResponse}.cs`; modify `Program.cs` (JWT auth like other services)
- Test: `AlertResponseTests.cs`, `AlertsQueryTests.cs`

**Interfaces:**
- Consumes: models, `IDetectionRule` list.
- Produces:
  - `AlertResponse(Guid AlertId, DateTimeOffset Timestamp, string RuleName, string Severity, string Status, string Description, IReadOnlyList<Guid> RelatedEvents, string? AffectedService, string? AffectedUser, string? AffectedEntity, string RecommendedAction)` with `static AlertResponse From(Alert a)`; serialised camelCase → exactly spec §6.4 field names. `RelatedEvents` ordered by event timestamp.
  - `GET /api/soc/alerts?status=&severity=` (newest first), `GET /api/soc/alerts/{id}` (404 if missing), `PATCH /api/soc/alerts/{id}/status` body `{ "status": "ACKNOWLEDGED" | "RESOLVED" }` (400 for any other value; 404 if missing).
  - `GET /api/soc/events?type=&user=&from=&to=&page=1&pageSize=50` (newest first, `pageSize` capped at 200) returning `SocEventResponse` with the 13 spec field names.
  - `GET /api/soc/rules` → `RuleDocResponse(RuleName, Purpose, InputEvents, DetectionCondition, Severity, AlertGenerated, RecommendedResponse)` per registered rule.
  - All `/api/soc/*` controllers `[Authorize(Roles = "Admin")]`.

- [ ] **Step 1: Write failing tests:**
  - `AlertResponse_From_MapsFieldsAndRelatedEvents` — serialise with web defaults; assert JSON property names are exactly `alertId, timestamp, ruleName, severity, status, description, relatedEvents, affectedService, affectedUser, affectedEntity, recommendedAction`.
  - `AlertStatusUpdate_RejectsOpen` — the status-validation helper returns false for `"OPEN"` and `"closed"`, true for `"ACKNOWLEDGED"`, `"RESOLVED"`.
  - `EventsQuery_FiltersByTypeAndUser_NewestFirst` (against `TestDb`).
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** controllers, DTOs, JWT bearer setup copied from PaymentService's `Program.cs`.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(soc): admin APIs for events, alerts and rule documentation`

---

### Task 9: Admin role and gateway routing

**Files:**
- Create: `backend/src/IdentityService/Services/AdminSeeder.cs` (verified end-to-end in Steps 3–4; IdentityService has no test project)
- Modify: `IdentityService/Program.cs`, `ApiGateway/Program.cs`, `ApiGateway/ocelot.json`, `docker-compose.yml`

**Interfaces:**
- Consumes: `PlayerRole.Admin` (already exists in `IdentityService/Models/Player.cs`), `/api/soc/*` (Task 8).
- Produces:
  - `AdminSeeder.EnsureAdminAsync(IdentityDbContext db, string email, string password, CancellationToken ct) -> Task` — if no player has that email, create one with `Role = Admin`, `Name = "SOC"`, `Surname = "Admin"`, `ClubId = "pcc"`, BCrypt password hash (same hashing as `AuthController.Register`). Existing player → unchanged. Called in `Program.cs` right after `Migrate()` when `Seed:AdminEmail` and `Seed:AdminPassword` are both set.
  - Gateway: `options.MapInboundClaims = false` on the `SquashPlatformAuth` JWT scheme (so the token's short `role` claim reaches Ocelot unmapped). New route in `ocelot.json`: upstream `/api/soc/{everything}` → downstream `/api/soc/{everything}`, `ServiceName: "soc-service"`, same `AuthenticationOptions`, `"RouteClaimsRequirement": { "role": "Admin" }`, `X-Forwarded-For` transform.
  - Gateway correlation middleware: after `await next()`, if `context.Response.StatusCode == 403`, emit `UNAUTHORISED_ACCESS` (MEDIUM, statusCode 403, endpoint/method/sourceIp from the request, message `"Forbidden: caller lacks the required role"`).

- [ ] **Step 1: Implement** the seeder, gateway changes and compose env vars (`Seed__AdminEmail`, `Seed__AdminPassword` on identity-service).
- [ ] **Step 2: Rebuild** — `docker compose up --build -d`.
- [ ] **Step 3: Verify registration cannot create an admin** — `POST /api/auth/register` with `"role": "Admin"` added to the body → the created account's login response shows `"role": "Player"`.
- [ ] **Step 4: Verify access control** via Postman:
  - Login as admin → `role` is `Admin`; `GET /api/soc/alerts` → 200.
  - Login as Meiring (Player) → `GET /api/soc/alerts` → 403, and `soc_db.events` gains an `UNAUTHORISED_ACCESS` row with `status_code = 403`.
  - No token → 401.
  - Existing Phase 1 flows (register, login, create booking, accept) still return their Phase 1 status codes.
- [ ] **Step 5: Commit** — `feat: Admin role, seeded admin account and admin-only SOC routes`

---

### Task 10: End-to-end demo assets and full verification

**Files:**
- Modify: `postman/PotchMatch-Demo.postman_collection.json` (new folder "Phase 2 — SOC"), `postman/PotchMatch-Demo.postman_environment.json` (`adminToken`, `alertId`)
- Create: `scripts/trigger-request-rate.ps1`

**Interfaces:**
- Consumes: all APIs above.
- Produces: Postman folder requests, in order: `Login Admin` (test script stores `adminToken`), `Failed Login x1` (wrong password for `wickusvn@gmail.com`; run 5× via Runner or click 5×), `List Open Alerts` (stores first alert's id as `alertId`), `Get Alert` (test asserts `relatedEvents.length >= 5` and `ruleName == "Multiple Failed Logins"`), `List Events (FAILED_LOGIN)`, `Get Rules` (asserts 4 rules), `Acknowledge Alert`, `Resolve Alert`, `Player calls SOC (expect 403)`. `trigger-request-rate.ps1` sends 120 `GET http://localhost:5132/api/bookings` requests (no token) as fast as possible and prints the count.

- [ ] **Step 1: Add** the Postman folder, environment variables and script.
- [ ] **Step 2: Full run on a clean stack** — `docker compose down -v`, `docker compose up --build -d`, wait for all services healthy, then:
  - Rule 1: run the folder through `List Open Alerts` → one HIGH `Multiple Failed Logins` alert with 5 related events. Repeat 3 more failed logins → still one alert, now 8 related events.
  - Rule 2: call `GET /api/bookings` 3× with an invalid token → MEDIUM `Unauthorised Endpoint Access` alert.
  - Rule 3: `powershell -File scripts/trigger-request-rate.ps1` → HIGH `POSSIBLE_DOS` alert (the 120 unauthenticated calls also trigger Rule 2; expected).
  - Rule 4: `docker compose stop payment-service` → `SERVICE_FAILURE` alert within ~30 s; `docker compose start payment-service` → that alert `RESOLVED` within ~30 s.
  - Resolve the Rule 1 alert via `Resolve Alert`, wait 30 s → no new Rule 1 alert appears.
- [ ] **Step 3: Run all unit tests** — `dotnet test backend/SquashPlatform.slnx` — Expected: all pass.
- [ ] **Step 4: Commit** — `test: Phase 2 SOC Postman folder and request-rate script`
