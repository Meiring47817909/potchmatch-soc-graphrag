# Cycle B — Neo4j Security Knowledge Graph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GraphService that owns a local Neo4j knowledge graph built from the app structure, Cycle A's SOC data and curated security knowledge, correlates alerts into incidents, and serves five named graph queries to admins.

**Architecture:** `neo4j:5-community` and a new .NET 10 `GraphService` join the compose stack. GraphService seeds app structure and security knowledge from JSON files at startup, then every 15 s pulls players (IdentityService) and alerts/events (SocService) through new key-protected internal endpoints, MERGEs them into Neo4j, and runs incident correlation. Admin-only query endpoints are routed through the gateway at `/api/graph/*`.

**Tech Stack:** .NET 10, ASP.NET Core, `Neo4j.Driver` 6.3.0, Serilog, Consul, Ocelot 24.1.0, xUnit, `Testcontainers.Neo4j` 4.16.0, EF Core InMemory (existing service tests), Docker Compose, Postman.

**Spec:** `docs/superpowers/specs/2026-10-08-cycle-b-knowledge-graph-design.md`

## Global Constraints

- Work only in `squash-platform-phase2`; `squash-platform` is never modified.
- `net10.0`; existing package versions unchanged; new packages: `Neo4j.Driver` 6.3.0, `Testcontainers.Neo4j` 4.16.0 (tests only), `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11, `Serilog.AspNetCore` 10.0.0, `Consul` 1.8.0.
- Neo4j image `neo4j:5-community`; ports `7474:7474`, `7687:7687`; `NEO4J_AUTH=neo4j/potchmatch-dev`; volume `neo4j-data`.
- Shared sync key env `GraphSync__Key` (dev value `dev-graph-sync-key-change-me`) on graph-service, soc-service, identity-service; header name `X-Graph-Sync-Key`; constant-time comparison; wrong/missing → 401.
- Internal sync endpoints are never routed through the gateway.
- Node keys exactly: Service `name`, Endpoint `key` (`"{METHOD} {pathTemplate}"`), Asset `name`, Role `name`, User `userId`, Event `eventId`, Alert `alertId`, Incident `incidentId`, Threat `id`, Control `id`, Vulnerability `id`, Document `id`, SyncState `name`.
- Relationship types exactly: TRIGGERED, GENERATED_BY, TARGETED, CREATED_ALERT, INDICATES, TARGETS, EXPLOITS, MITIGATES, CONTAINS, DEPENDS_ON, EXPOSES, HAS_ROLE, DESCRIBES, SUPPORTS_RESPONSE_TO, AFFECTS, USES, REQUIRES_ROLE.
- Rule→threat map: `Multiple Failed Logins`→`T1110`, `Unauthorised Endpoint Access`→`T1078`, `POSSIBLE_DOS`→`T1499`, `SERVICE_FAILURE`→`T1499`.
- Incident window default 30 min (`Incidents:WindowMinutes`); sync interval 15 s (`GraphSync:IntervalSeconds`); players every 60 s (`GraphSync:PlayersIntervalSeconds`).
- Seed files live in repo-root `graph-data/`, mounted read-only into graph-service at `/app/graph-data` (config `GraphSeed:Directory`).
- All writes are idempotent MERGEs; re-running anything never duplicates nodes or relationships.
- Every EF Core migration (none expected) keeps its `.Designer.cs`. Follow the per-service duplication convention (no shared library).
- Commit trailers:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_019wuPNJ5g7LKK4idzgvacog
  ```

**Plan refinement over the spec (tie-safe cursors):** a cursor of only a timestamp skips items that share the boundary timestamp when a page is cut. Both SocService sync endpoints therefore take an extra `afterId` and filter on `(timestamp, id) > (since, afterId)`; the cursor stored in `SyncState` is `{at, id}`. Everything else in spec §5.2 is unchanged.

## Review Focus

1. **Page boundary with equal timestamps** — alerts/events sharing the cursor timestamp must not be skipped when a page is cut there → Task 2 `SyncAlerts_TieAtPageBoundary_NothingSkipped`.
2. **Failed login typed with different case/whitespace** (`" Wickusvn@Gmail.com"`) must link to the existing account's User, not create an unknown one → Task 5 `UserMatcher_NormalisesEmail` + Task 7 `Sync_FailedLoginEmailCase_LinksToExistingUser`.
3. **An alert synced again after it gained events or changed status** must update in place with no duplicate Alert, Event or relationship → Task 7 `Sync_SameAlertTwice_IsIdempotent`.
4. **Raw paths with GUIDs, numbers and query strings** must collapse onto one Endpoint template, not create one discovered endpoint per request → Task 5 `Normalise_GuidNumberAndQuery`.
5. **A wiped Neo4j volume** (no SyncState) must trigger a full resync from the beginning, not a stall → Task 7 `Sync_NoSyncState_FetchesFromStart`.

---

## File Structure

**New**
- `graph-data/README.md` — how seeds work + Neo4j Desktop remote connection steps.
- `graph-data/app-structure.json` — services, endpoints, assets, roles, structural relationships, threat targets.
- `graph-data/knowledge/README.md`, `sources/*.md`, `export.cypher` — Graph Builder pipeline (user-run).
- `graph-data/knowledge/security-knowledge.json` — fixture now, curated export at Task 11.
- `backend/src/GraphService/` — `GraphService.csproj`, `Dockerfile`, `.dockerignore`, `appsettings.json`, `Program.cs`;
  - `Graph/GraphSchema.cs` (labels, key properties, relationship types), `Graph/Neo4jConnector.cs` (connect + retry + constraints);
  - `Seeding/SeedFile.cs`, `Seeding/SeedValidator.cs`, `Seeding/SeedLoader.cs`;
  - `Mapping/EndpointNormaliser.cs`, `Mapping/RuleThreatMap.cs`, `Mapping/UserMatcher.cs`, `Mapping/SubjectResolver.cs`;
  - `Sync/SyncDtos.cs`, `Sync/SocSyncClient.cs`, `Sync/IdentitySyncClient.cs`, `Sync/GraphWriter.cs`, `Sync/SyncWorker.cs`;
  - `Incidents/IncidentCorrelator.cs`, `Incidents/IncidentWriter.cs`;
  - `Queries/GraphQueryResult.cs`, `Queries/GraphQueries.cs`, `Queries/GraphQueryRunner.cs`, `Controllers/GraphController.cs`.
- `backend/tests/GraphService.Tests/` — unit tests + `Neo4jFixture.cs` (Testcontainers) + integration tests.
- `backend/src/SocService/Controllers/InternalSyncController.cs`, `backend/src/SocService/Queries/SyncQuery.cs`, `backend/src/SocService/Dtos/SyncDtos.cs`.
- `backend/src/IdentityService/Controllers/InternalSyncController.cs`, `backend/src/IdentityService/Logging/SyncKeyValidator.cs`; `backend/tests/IdentityService.Tests/`.

**Modified**
- `backend/docker-compose.yml`, `backend/SquashPlatform.slnx`, `backend/src/ApiGateway/ocelot.json`, `backend/src/*/appsettings.json` (GraphSync key placeholders), `postman/PotchMatch-Demo.postman_collection.json` (+ environment).

---

### Task 1: Security-knowledge sources and Graph Builder instructions

**Files:**
- Create: `graph-data/knowledge/README.md`, `graph-data/knowledge/export.cypher`, `graph-data/knowledge/sources/*.md`

**Interfaces:**
- Produces: source documents and an export query the user runs; nothing else depends on them until Task 11.

- [ ] **Step 1: Write sources** — one markdown file each (header lines: `Title:`, `Source URL:`, `Attribution/licence:`), concise paraphrase of the official content (MITRE ATT&CK® © The MITRE Corporation, reproduced/paraphrased under the ATT&CK terms of use; OWASP CC BY-SA 4.0):
  `mitre-T1110-brute-force.md` (incl. .001 Password Guessing, .003 Password Spraying, .004 Credential Stuffing), `mitre-T1078-valid-accounts.md`, `mitre-T1499-endpoint-dos.md`, `mitre-M1036-account-use-policies.md`, `mitre-M1032-mfa.md`, `mitre-M1027-password-policies.md`, `mitre-M1037-filter-network-traffic.md`, `owasp-A01-broken-access-control.md`, and four PotchMatch procedures `procedure-password-reset.md`, `procedure-session-revocation.md`, `procedure-ip-blocking.md`, `procedure-service-outage-runbook.md` (each: purpose, trigger alerts, numbered steps, which threat it responds to, which control it implements).
- [ ] **Step 2: Write `export.cypher`** — a single read query for Aura returning `{nodes:[{label, name, id, description, properties}], relationships:[{type, fromLabel, fromName, toLabel, toName}]}` restricted to labels Threat, Control, Vulnerability, Document and types MITIGATES, EXPLOITS, DESCRIBES, SUPPORTS_RESPONSE_TO (no `Chunk`, no `embedding` properties).
- [ ] **Step 3: Write `README.md`** — numbered steps: create Aura instance connection in https://llm-graph-builder.neo4jlabs.com/, set Graph Schema to exactly those 4 labels and 4 relationship types, upload all `sources/*.md`, run extraction, open Aura Query and run `export.cypher`, download result as JSON, save to `graph-data/knowledge/security-knowledge.raw.json`, tell Claude.
- [ ] **Step 4: Commit** — `docs(graph): security-knowledge sources and Graph Builder export pipeline`

---

### Task 2: SocService internal sync endpoints

**Files:**
- Create: `backend/src/SocService/Dtos/SyncDtos.cs`, `backend/src/SocService/Queries/SyncQuery.cs`, `backend/src/SocService/Controllers/InternalSyncController.cs`
- Modify: `backend/src/SocService/appsettings.json` (`"GraphSync": { "Key": "" }`), `backend/src/SocService/Program.cs` (register `SyncQuery`)
- Test: `backend/tests/SocService.Tests/SyncQueryTests.cs`, `InternalSyncKeyTests.cs`

**Interfaces:**
- Consumes: `SocDbContext`, `SocEventResponse.From(SocEvent)`, `IngestKeyValidator.IsValid(string? provided, string expected)`.
- Produces (HTTP, consumed by Task 6):
  - `GET /api/internal/sync/alerts?updatedSince={ISO}&afterId={guid}&limit={n}` → `SyncAlertResponse[]`:
    `record SyncAlertResponse(Guid AlertId, DateTimeOffset Timestamp, DateTimeOffset UpdatedAt, string RuleName, string Severity, string Status, string Description, string? AffectedService, string? AffectedUser, string? AffectedEntity, string RecommendedAction, IReadOnlyList<SocEventResponse> RelatedEvents)`.
    Filter `(UpdatedAt, AlertId) > (updatedSince, afterId)` (both omitted ⇒ all; `afterId` omitted ⇒ `Guid.Empty`); order `UpdatedAt, AlertId`; `limit` default 200, clamp 1..500. `RelatedEvents` ordered by event timestamp.
  - `GET /api/internal/sync/events?receivedAfter={ISO}&afterId={guid}&limit={n}` → `SyncEventResponse[]`: `record SyncEventResponse(SocEventResponse Event, DateTimeOffset ReceivedAt)`. Excludes `EventType == "REQUEST_RECEIVED"` and events present in `AlertEvents`. Filter `(ReceivedAt, EventId) > (receivedAfter, afterId)`; order `ReceivedAt, EventId`; `limit` default 500, clamp 1..1000.
  - Both: `[AllowAnonymous]`, header `X-Graph-Sync-Key` checked against `GraphSync:Key` with `IngestKeyValidator.IsValid`; 401 otherwise. Timestamps normalised to UTC before querying.
  - `SyncQuery.GetAlertsAsync(DateTimeOffset? since, Guid? afterId, int limit, CancellationToken)`, `SyncQuery.GetEventsAsync(DateTimeOffset? since, Guid? afterId, int limit, CancellationToken)` — the controller is a thin wrapper.

- [ ] **Step 1: Write failing tests** (InMemory via `TestDb.Create()`):
  - `SyncAlerts_ReturnsUpdatedSinceInOrderWithFullEvents` — 3 alerts at t1<t2<t3, since=t1 → [t2,t3]; each `RelatedEvents` holds full event objects ordered by timestamp.
  - `SyncAlerts_TieAtPageBoundary_NothingSkipped` — 3 alerts with identical `UpdatedAt`; page 1 `limit=2`, page 2 using last item's `(UpdatedAt, AlertId)` → 1 item; union = all 3.
  - `SyncAlerts_LimitClamped` — limit 10000 → at most 500.
  - `SyncEvents_ExcludesRequestReceivedAndAlertLinked` — FAILED_LOGIN unlinked (included), FAILED_LOGIN linked to an alert (excluded), REQUEST_RECEIVED (excluded).
  - `SyncEvents_TieAtPageBoundary_NothingSkipped`.
  - `SyncEvents_NonUtcCursor_Normalised` — `receivedAfter` with +02:00 offset filters the same instant.
  - `InternalSync_KeyValidation` — controller action with no/wrong header → `UnauthorizedResult`; correct → `OkObjectResult` (construct controller with `DefaultHttpContext`).
- [ ] **Step 2: Run** `dotnet test backend/tests/SocService.Tests --filter "Sync"` — Expected: FAIL (types missing).
- [ ] **Step 3: Implement** DTOs, `SyncQuery`, `InternalSyncController` (`[Route("api/internal/sync")]`).
- [ ] **Step 4: Run** `dotnet test backend/tests/SocService.Tests` — Expected: all pass.
- [ ] **Step 5: Commit** — `feat(soc): key-protected internal sync endpoints for the graph`

---

### Task 3: IdentityService internal players endpoint

**Files:**
- Create: `backend/src/IdentityService/Controllers/InternalSyncController.cs`, `backend/src/IdentityService/Logging/SyncKeyValidator.cs`
- Create test project: `backend/tests/IdentityService.Tests/{IdentityService.Tests.csproj,TestDb.cs,InternalSyncTests.cs}`
- Modify: `backend/src/IdentityService/appsettings.json` (`"GraphSync": { "Key": "" }`), `backend/SquashPlatform.slnx`

**Interfaces:**
- Produces: `GET /api/internal/sync/players` → `record SyncPlayerResponse(Guid Id, string Email, string Name, string Surname, string Role)[]` — all players **including** Admin (the graph needs the admin user), ordered by email. `[AllowAnonymous]` + `X-Graph-Sync-Key` vs `GraphSync:Key` via `SyncKeyValidator.IsValid(string? provided, string expected)` (copy of SocService's `IngestKeyValidator` logic per the duplication convention); 401 otherwise.

- [ ] **Step 1: Create the test project** (xUnit + EF InMemory 10.0.11, same versions as SocService.Tests) and add it to the slnx under `/tests/`.
- [ ] **Step 2: Write failing tests:** `SyncPlayers_ReturnsAllIncludingAdmin`, `SyncPlayers_WrongKey_401`, `SyncPlayers_EmptyConfiguredKey_401`, `SyncKeyValidator_RejectsNullEmptyWrong_AcceptsExact`.
- [ ] **Step 3: Run** `dotnet test backend/tests/IdentityService.Tests` — Expected: FAIL.
- [ ] **Step 4: Implement**.
- [ ] **Step 5: Run** — Expected: PASS.
- [ ] **Step 6: Commit** — `feat(identity): key-protected internal players sync endpoint`

---

### Task 4: GraphService skeleton, Neo4j in compose, schema constraints

**Files:**
- Create: `backend/src/GraphService/{GraphService.csproj,Dockerfile,.dockerignore,appsettings.json,Program.cs}`, `Graph/GraphSchema.cs`, `Graph/Neo4jConnector.cs`
- Create: `backend/tests/GraphService.Tests/{GraphService.Tests.csproj,Neo4jFixture.cs,ConstraintTests.cs,GraphSchemaTests.cs}`
- Modify: `backend/SquashPlatform.slnx`, `backend/docker-compose.yml`

**Interfaces:**
- Produces:
  - `static class GraphSchema` — `IReadOnlyDictionary<string,string> KeyProperty` (label → key property, exactly the Global Constraints list), `IReadOnlySet<string> RelationshipTypes` (exactly the 17 types), `bool IsKnownLabel(string)`, `bool IsKnownRelationship(string)`.
  - `class Neo4jConnector(IDriver driver, ILogger<Neo4jConnector> log)` — `Task WaitUntilAvailableAsync(CancellationToken)` (retry `VerifyConnectivityAsync` with exponential backoff 1 s → max 30 s, logging each failure), `Task EnsureConstraintsAsync(CancellationToken)` (one `CREATE CONSTRAINT {label}_{key}_unique IF NOT EXISTS FOR (n:{label}) REQUIRE n.{key} IS UNIQUE` per label).
  - `IDriver` registered as singleton from `Neo4j:Uri`, `Neo4j:User`, `Neo4j:Password`.
  - Test fixture `Neo4jFixture : IAsyncLifetime` (Testcontainers `Neo4jBuilder` image `neo4j:5-community`) exposing `IDriver Driver` and `Task ResetAsync()` (`MATCH (n) DETACH DELETE n`); xUnit collection `"neo4j"`.
- Program.cs follows SocService's pattern: Serilog (`ServiceName = "graph-service"`), health checks, Consul registration `graph-service` with `DeregisterCriticalServiceAfter = 1 min`, JWT bearer (copy SocService block, default claim mapping), controllers. On start: `WaitUntilAvailableAsync` → `EnsureConstraintsAsync` inside a hosted startup service (Task 6's worker waits for it).
- Compose: `neo4j` (image, ports, `NEO4J_AUTH`, volume, healthcheck `wget -qO- http://localhost:7474 || exit 1`); `graph-service` (build `./src/GraphService`, depends on healthy `neo4j` + `consul`, env `Neo4j__Uri=bolt://neo4j:7687`, `Neo4j__User=neo4j`, `Neo4j__Password=potchmatch-dev`, `GraphSync__Key`, `GraphSync__SocUrl=http://soc-service:8080`, `GraphSync__IdentityUrl=http://identity-service:8080`, `GraphSeed__Directory=/app/graph-data`, Consul/ServiceRegistration vars like soc-service, volume `../graph-data:/app/graph-data:ro`); add `GraphSync__Key` to soc-service and identity-service.

- [ ] **Step 1: Write failing tests:** `GraphSchema_HasAllTwelveSpecLabelsWithKeys` (asserts the 12 spec labels + SyncState and their keys), `GraphSchema_HasAll17RelationshipTypes`, and (collection `neo4j`) `EnsureConstraints_CreatesOnePerLabel_Idempotent` (run twice; `SHOW CONSTRAINTS` count == 13).
- [ ] **Step 2: Run** `dotnet test backend/tests/GraphService.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement** as specified.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Verify** compose: build images (sandbox: workspace cloud-build helper), `docker compose up -d --no-build`; `docker compose ps` shows neo4j healthy and graph-service running; Consul lists `graph-service` passing; `docker compose exec neo4j cypher-shell -u neo4j -p potchmatch-dev "SHOW CONSTRAINTS YIELD name RETURN count(*)"` → 13.
- [ ] **Step 6: Commit** — `feat(graph): GraphService skeleton with Neo4j and schema constraints`

---

### Task 5: Pure mapping helpers

**Files:**
- Create: `backend/src/GraphService/Mapping/{EndpointNormaliser,RuleThreatMap,UserMatcher,SubjectResolver}.cs`
- Test: `backend/tests/GraphService.Tests/MappingTests.cs`

**Interfaces:**
- Produces:
  - `static string? EndpointNormaliser.ToKey(string? method, string? rawPath)` — null if either missing; drops query string; segments that parse as `Guid` or integer become `{id}`; trailing slash removed; method upper-cased; returns `"{METHOD} {path}"`.
  - `static bool RuleThreatMap.TryGetThreatId(string ruleName, out string threatId)` — the 4 mappings.
  - `static string UserMatcher.EmailKey(string email)` (trim + lower-invariant); `static bool UserMatcher.LooksLikeEmail(string userId)` (contains `@`, not a Guid); `static string UserMatcher.UnknownUserId(string email)` → `"email:" + EmailKey(email)`.
  - `static string SubjectResolver.Resolve(string? affectedUserId, IReadOnlyList<string> triggeringUserIds, IReadOnlyList<string> sourceIps, string? affectedService)` — `user:{affectedUserId}` if set, else `user:{first triggering user (ordinal min)}`, else `ip:{most frequent ip (ties → ordinal min)}`, else `service:{affectedService}`, else `"unknown"`.

- [ ] **Step 1: Write failing tests:** `Normalise_GuidNumberAndQuery` (`("get","/api/players/5e9a8f2e-0000-0000-0000-000000000001?x=1")` → `"GET /api/players/{id}"`; `("PUT","/api/payments/42/refund/")` → `"PUT /api/payments/{id}/refund"`); `Normalise_NullInputs_ReturnNull`; `RuleThreatMap_AllFourRules` + `RuleThreatMap_Unknown_False`; `UserMatcher_NormalisesEmail` (`" Wickusvn@Gmail.com "` → `"wickusvn@gmail.com"`), `UserMatcher_LooksLikeEmail`; `Subject_PrefersAffectedUser`, `Subject_FallsBackToTriggeringUser`, `Subject_MostFrequentIp`, `Subject_Service`, `Subject_Unknown`.
- [ ] **Step 2: Run** `dotnet test backend/tests/GraphService.Tests --filter Mapping` — Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(graph): endpoint, rule, user and subject mapping helpers`

---

### Task 6: Seed loader, app-structure seed, knowledge fixture

**Files:**
- Create: `backend/src/GraphService/Seeding/{SeedFile,SeedValidator,SeedLoader}.cs`, `graph-data/app-structure.json`, `graph-data/knowledge/security-knowledge.json` (fixture), `graph-data/README.md`
- Test: `backend/tests/GraphService.Tests/SeedValidatorTests.cs`, `SeedLoaderTests.cs`

**Interfaces:**
- Consumes: `GraphSchema` (Task 4), `IDriver`.
- Produces:
  - `record SeedFile(IReadOnlyList<SeedNode> Nodes, IReadOnlyList<SeedRelationship> Relationships)`, `record SeedNode(string Label, string Key, IReadOnlyDictionary<string, JsonElement> Properties)`, `record SeedRelationship(string Type, NodeRef From, NodeRef To)`, `record NodeRef(string Label, string Key)` — JSON camelCase per spec §5.3.
  - `static IReadOnlyList<string> SeedValidator.Validate(SeedFile file, IReadOnlySet<NodeRef> alreadyLoaded)` — errors for: unknown label, unknown relationship type, empty key, duplicate node, relationship endpoint not in file ∪ `alreadyLoaded`. Error text names the item.
  - `class SeedLoader(IDriver, ILogger)` — `Task<IReadOnlySet<NodeRef>> LoadAsync(string path, IReadOnlySet<NodeRef> alreadyLoaded, CancellationToken)`; throws `SeedValidationException(string path, IReadOnlyList<string> errors)` before writing anything; MERGEs nodes by label/key and `SET n += properties`, MERGEs relationships. Program loads `app-structure.json` then `security-knowledge.json` (passing the first's refs) after constraints.
  - `app-structure.json` content: exactly spec §5.1 (services with `kind`; every endpoint listed there with `method`, `path`, `protected`; EXPOSES from owning service; assets + USES; roles + REQUIRES_ROLE Admin on `/api/soc/*` and `/api/graph/*` endpoints; DEPENDS_ON; TARGETS for T1110/T1078/T1499). Because TARGETS references Threat nodes from the knowledge seed, the loader order is app-structure **nodes and non-threat relationships** first; resolve this by putting the three Threat stub nodes (`T1110`, `T1078`, `T1499`, `name` only) in `app-structure.json` — the knowledge seed MERGEs onto them.
  - Knowledge fixture: Threats T1110/T1078/T1499; Controls M1036, M1032, M1027, M1037; Vulnerabilities `VULN-WEAK-PASSWORDS`, `VULN-BROKEN-ACCESS-CONTROL`, `VULN-NO-RATE-LIMIT`; Documents `DOC-T1110`, `DOC-PASSWORD-RESET`, `DOC-SESSION-REVOCATION`, `DOC-IP-BLOCKING`, `DOC-SERVICE-OUTAGE`; MITIGATES (M1036,M1032,M1027→T1110; M1032→T1078; M1037→T1499), EXPLOITS (T1110→VULN-WEAK-PASSWORDS; T1078→VULN-BROKEN-ACCESS-CONTROL; T1499→VULN-NO-RATE-LIMIT), DESCRIBES (DOC-PASSWORD-RESET→M1027, DOC-SESSION-REVOCATION→M1032, DOC-IP-BLOCKING→M1037), SUPPORTS_RESPONSE_TO (DOC-PASSWORD-RESET→T1110, DOC-SESSION-REVOCATION→T1078, DOC-IP-BLOCKING→T1499, DOC-SERVICE-OUTAGE→T1499); every node `origin: "fixture"`.
  - `graph-data/README.md`: seed format, load order, and Neo4j Desktop remote connection (`bolt://localhost:7687`, `neo4j` / `potchmatch-dev`), Browser at `http://localhost:7474`.

- [ ] **Step 1: Write failing tests:** validator — `UnknownLabel_Error`, `UnknownRelationship_Error`, `DanglingRelationship_Error`, `RelationshipToAlreadyLoaded_Ok`, `DuplicateNode_Error`; loader (collection `neo4j`) — `Load_RealSeedFiles_ExpectedCounts` (loads both repo files; asserts every spec label except User/Event/Alert/Incident has ≥1 node and every structural/knowledge relationship type exists), `Load_Twice_IsIdempotent` (node + relationship counts equal after second load), `Load_InvalidFile_ThrowsAndWritesNothing`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** loader + write the three data files.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(graph): validated seed loader with app-structure and knowledge seeds`

---

### Task 7: Sync clients, graph writer and sync worker

**Files:**
- Create: `backend/src/GraphService/Sync/{SyncDtos,SocSyncClient,IdentitySyncClient,GraphWriter,SyncWorker}.cs`
- Test: `backend/tests/GraphService.Tests/GraphWriterTests.cs`, `SyncWorkerTests.cs`

**Interfaces:**
- Consumes: Task 2/3 HTTP contracts; Task 5 helpers; `IDriver`.
- Produces:
  - DTOs mirroring Task 2/3 JSON: `SyncAlert`, `SyncEvent` (13 spec fields), `SyncEventEnvelope(SyncEvent Event, DateTimeOffset ReceivedAt)`, `SyncPlayer(Guid Id, string Email, string Name, string Surname, string Role)`.
  - `interface ISocSyncClient { Task<IReadOnlyList<SyncAlert>> GetAlertsAsync(SyncCursor? after, int limit, CancellationToken); Task<IReadOnlyList<SyncEventEnvelope>> GetEventsAsync(SyncCursor? after, int limit, CancellationToken); }`, `interface IIdentitySyncClient { Task<IReadOnlyList<SyncPlayer>> GetPlayersAsync(CancellationToken); }` — typed `HttpClient`s adding `X-Graph-Sync-Key`; non-2xx throws `HttpRequestException`.
  - `record SyncCursor(DateTimeOffset At, Guid Id)`.
  - `class GraphWriter(IDriver)`:
    - `Task UpsertPlayersAsync(IReadOnlyList<SyncPlayer>, CancellationToken)` — MERGE `User {userId: id}` SET email (EmailKey), name (`"{Name} {Surname}"`), `unknownAccount=false`; replace `HAS_ROLE` with the current role; if an `unknownAccount` User exists with the same email, move its TRIGGERED relationships to the real user and delete it.
    - `Task UpsertAlertAsync(SyncAlert, CancellationToken)` — one transaction: MERGE Alert (SET all properties), its events (each via the event rules below), `CREATED_ALERT`, `INDICATES` (via `RuleThreatMap`; log warning if unmapped), `AFFECTS`.
    - `Task UpsertEventsAsync(IReadOnlyList<SyncEvent>, CancellationToken)` — one transaction per batch.
    - Event rules (spec §4.3): MERGE Event SET properties; `GENERATED_BY` Service; `TARGETED` Endpoint via `EndpointNormaliser.ToKey` (MERGE with `discovered: true` on create); `TRIGGERED` from User: Guid userId → MERGE `User {userId}`; email userId → match `User {email: EmailKey}` else MERGE `User {userId: UnknownUserId(email), email, unknownAccount: true}`.
    - `Task<SyncCursor?> GetCursorAsync(string name, CancellationToken)`, `Task SetCursorAsync(string name, SyncCursor cursor, CancellationToken)` on `SyncState {name}`.
  - `class SyncWorker : BackgroundService` — `Task RunOnceAsync(DateTimeOffset now, CancellationToken)`: players if `PlayersIntervalSeconds` elapsed → alerts (page until fewer than limit; per alert upsert then set cursor `alerts` to `(UpdatedAt, AlertId)`; on exception log and stop this phase) → events (same, cursor `events` = `(ReceivedAt, EventId)`, batch upsert then cursor = last item) → `IncidentWriter.CorrelateAsync` (Task 8; until then a no-op hook). Client exceptions are logged and leave cursors unchanged. Waits for the startup service (Task 4 constraints, extended in Task 6 with seed loading) to finish before the first run.

- [ ] **Step 1: Write failing tests** (collection `neo4j`, fakes for the two clients):
  - `Sync_AlertWithEvents_CreatesFullChain` — FAILED_LOGIN×5 alert for `wickusvn@gmail.com` (player exists via players sync) → `(User)-[:TRIGGERED]->(Event)-[:CREATED_ALERT]->(Alert)-[:INDICATES]->(Threat {id:'T1110'})`, `(Event)-[:GENERATED_BY]->(Service {name:'identity-service'})`, `(Event)-[:TARGETED]->(Endpoint {key:'POST /api/auth/login'})`, `(Alert)-[:AFFECTS]->(Service)`.
  - `Sync_SameAlertTwice_IsIdempotent` — run twice (second time alert has status RESOLVED and one more event) → 1 Alert with status RESOLVED, event count +1, no duplicate relationships (`count(r)` per type unchanged except the new event's).
  - `Sync_FailedLoginEmailCase_LinksToExistingUser` — event userId `" Wickusvn@Gmail.com"` → TRIGGERED from the real user; no `unknownAccount` user.
  - `Sync_UnknownEmail_CreatesUnknownUser_ThenMergesWhenRegistered` — then players sync adds that email → TRIGGERED moved to real user, unknown node gone.
  - `Sync_RoleChange_ReplacesHasRole`.
  - `Sync_NoSyncState_FetchesFromStart` — fresh graph → client called with `after == null`.
  - `Sync_ClientThrows_CursorUnchanged`.
  - `Sync_UnmappedRule_LoadsWithoutIndicates`.
  - `Sync_UnknownEndpoint_CreatesDiscovered`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement**; register clients (`HttpClient` base addresses from `GraphSync:SocUrl` / `GraphSync:IdentityUrl`), writer, worker.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(graph): pull sync of players, alerts and events into Neo4j`

---

### Task 8: Incident correlation

**Files:**
- Create: `backend/src/GraphService/Incidents/{IncidentCorrelator,IncidentWriter}.cs`; modify `SyncWorker.cs` to call it
- Test: `backend/tests/GraphService.Tests/IncidentCorrelatorTests.cs`, `IncidentWriterTests.cs`

**Interfaces:**
- Consumes: `SubjectResolver.Resolve` (Task 5), `IDriver`.
- Produces:
  - `record AlertForCorrelation(string AlertId, DateTimeOffset Timestamp, string RuleName, string Severity, string Status, string Subject)`, `record OpenIncident(string IncidentId, string Subject, DateTimeOffset LastAlertAt, string Status)`, `record IncidentAssignment(string IncidentId, string AlertId, string Subject)`.
  - `static IReadOnlyList<IncidentAssignment> IncidentCorrelator.Assign(IReadOnlyList<AlertForCorrelation> uncontained, IReadOnlyList<OpenIncident> existing, TimeSpan window)` — alerts in `(Timestamp, AlertId)` order; join the non-RESOLVED incident with the same subject whose `LastAlertAt >= alert.Timestamp - window` (updating its `LastAlertAt` as it goes), else new incident with id `IncidentIds.For(subject, alertId)`.
  - `static string IncidentIds.For(string subject, string firstAlertId)` → `"INC-" + first 12 lower-hex chars of SHA-256(UTF-8(subject + firstAlertId))`.
  - `class IncidentWriter(IDriver, IOptions<IncidentOptions>)` — `Task CorrelateAsync(CancellationToken)`: read alerts with no incoming CONTAINS (with subject inputs gathered in Cypher: affectedUser resolved by §4.3 matching, triggering userIds, event sourceIps, affectedService), read existing incidents, call `Assign`, MERGE Incident + CONTAINS, then recompute derived properties for every touched incident: `severity` (HIGH>MEDIUM>LOW), `status` (OPEN if any contained alert OPEN/ACKNOWLEDGED else RESOLVED), `title` (`"Activity from {subject}: {distinct ruleNames sorted, comma-separated}"`), `escalated` (distinct rules ≥ 2), `priority` (`HIGH` if escalated else severity), `openedAt`, `lastAlertAt`, `subject`. Also recompute status for incidents whose alerts changed status (all incidents with any contained alert updated since last run — simplest correct: recompute derived props for all non-RESOLVED incidents each run).

- [ ] **Step 1: Write failing tests:**
  - Correlator: `SameSubjectWithinWindow_Joins`, `SameSubjectOutsideWindow_NewIncident` (31 min), `ExactlyAtWindow_Joins` (30 min), `ResolvedIncident_NotReused`, `DifferentSubjects_Separate`, `Ids_Deterministic` (same input twice → same ids).
  - Writer (collection `neo4j`): `Correlate_FailedLoginsThenUnauthorisedSameIp_EscalatedIncident` (Rule 1 IP alert + Rule 2 IP alert, same IP, 5 min apart → one Incident `escalated=true`, `priority='HIGH'`, 2 CONTAINS), `Correlate_Twice_NoDuplicates`, `Correlate_AllAlertsResolved_IncidentResolved`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement**; bind `Incidents:WindowMinutes` (default 30).
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(graph): incident correlation with escalation`

---

### Task 9: Graph query API (five queries + incidents)

**Files:**
- Create: `backend/src/GraphService/Queries/{GraphQueryResult,GraphQueries,GraphQueryRunner}.cs`, `Controllers/GraphController.cs`
- Test: `backend/tests/GraphService.Tests/GraphQueriesTests.cs`

**Interfaces:**
- Consumes: `IDriver`; graph shape from Tasks 6–8.
- Produces:
  - `record GraphNodeDto(string Id, string Label, IReadOnlyDictionary<string, object?> Properties)`, `record GraphRelDto(string Id, string Type, string Start, string End)`, `record GraphQueryResult(string Query, string Cypher, IReadOnlyDictionary<string, object?> Parameters, IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphRelDto> Relationships, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows)` — camelCase JSON matching spec §6.
  - `static class GraphQueries` — const name + Cypher for: `alerts-affecting-service`, `users-high-severity`, `threats-for-service`, `controls-for-alert`, `dependency-impact`, `incidents`, `incident-detail`. Each Cypher returns the paths/nodes it traverses (so nodes and relationships can be collected) plus row columns.
  - `class GraphQueryRunner(IDriver)` — `Task<GraphQueryResult> RunAsync(string name, string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken)`: collects every `INode`, `IRelationship` and `IPath` in each record into de-duplicated `Nodes`/`Relationships` (Id = `ElementId`, Label = first label, properties converted to JSON-safe values: temporal → ISO string), and scalar columns into `Rows`.
  - `GraphController` `[Route("api/graph")] [Authorize(Roles = "Admin")]`, GET only: `services/{name}/alerts`, `users/high-severity`, `services/{name}/threats`, `alerts/{id}/controls`, `services/{name}/impact`, `incidents?status=`, `incidents/{id}`. Unknown service/alert/incident (existence check first) → 404; `ServiceUnavailableException`/`SessionExpiredException` → 503.

- [ ] **Step 1: Write failing tests** (collection `neo4j`; fixture = both seeds + a synced FAILED_LOGIN alert via `GraphWriter` + an escalated incident via `IncidentWriter`):
  - `AlertsAffectingService_IdentityService_ReturnsAlert`.
  - `UsersHighSeverity_ReturnsWickusWithCount`.
  - `ThreatsForService_IdentityService_IncludesT1110AndT1078`.
  - `ControlsForAlert_FailedLogins_ReturnsT1110Controls` — rows include M1036, M1032, M1027; nodes include `DOC-PASSWORD-RESET`; relationships include MITIGATES and SUPPORTS_RESPONSE_TO.
  - `DependencyImpact_PaymentService_IncludesBookingAndGateway` — booking-service at depth 1, api-gateway at depth 1 (direct) — assert both present with their path lengths.
  - `Incidents_ListAndDetail`.
  - `Result_NodesAndRelationshipsDeduplicated` and `Result_IncludesCypherAndParameters`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** runner, queries, controller.
- [ ] **Step 4: Run** `dotnet test backend/SquashPlatform.slnx` — Expected: all pass.
- [ ] **Step 5: Commit** — `feat(graph): admin graph query API with evidence-shaped results`

---

### Task 10: Gateway route, end-to-end verification and Postman

**Files:**
- Modify: `backend/src/ApiGateway/ocelot.json`, `postman/PotchMatch-Demo.postman_collection.json`, `postman/PotchMatch-Demo.postman_environment.json`

**Interfaces:**
- Produces: gateway route upstream `/api/graph/{everything}` → downstream `/api/graph/{everything}`, `ServiceName: "graph-service"`, `UpstreamHttpMethod: ["Get"]`, same `AuthenticationOptions`, `"RouteClaimsRequirement": { "role": "Admin" }`, `X-Forwarded-For` transform (preserve existing line endings; only add the route). Postman folder "Phase 2 — Graph": Login Admin (reuse `adminToken`), Users linked to HIGH alerts, Alerts affecting identity-service, Controls for alert (`{{graphAlertId}}` set from the users query or `/api/soc/alerts`), Threats for identity-service, Dependency impact of payment-service, Incidents, Incident detail, Player calls graph (expect 403). Env vars `graphAlertId`, `incidentId`.

- [ ] **Step 1: Implement** route + Postman folder.
- [ ] **Step 2: End-to-end on a clean stack** — `docker compose down -v`, build changed images, `up -d --no-build`, wait healthy, then:
  - 5 failed logins for `wickusvn@gmail.com` (registered first) → within 45 s `GET /api/graph/users/high-severity` lists that user; `alerts/{id}/controls` returns T1110 with M1036, M1032, M1027 and `DOC-PASSWORD-RESET`.
  - 3 requests with an invalid token from the same client → `GET /api/graph/incidents` shows an incident with `escalated: true`.
  - `services/payment-service/impact` lists booking-service and api-gateway.
  - Player token on `/api/graph/incidents` → 403; no token → 401.
  - `cypher-shell` count check: `MATCH (n) RETURN labels(n)[0], count(*)` shows all 12 spec labels.
  - Capture real outputs in the report. Leave stack down.
- [ ] **Step 3: Run** `dotnet test backend/SquashPlatform.slnx` — Expected: all pass.
- [ ] **Step 4: Commit** — `feat(gateway): admin-only /api/graph route; Postman graph folder`

---

### Task 11: Replace the knowledge fixture with the curated Graph Builder export

**Prerequisite (user):** `graph-data/knowledge/security-knowledge.raw.json` exists (Task 1 README steps). If it doesn't yet, this task waits; Tasks 1–10 do not.

**Files:**
- Modify: `graph-data/knowledge/security-knowledge.json`
- Create: `graph-data/knowledge/CURATION.md`

**Interfaces:**
- Consumes: raw export format from Task 1 `export.cypher`; seed format and validator from Task 6.
- Produces: curated seed with the same IDs the rest of the system relies on (`T1110`, `T1078`, `T1499`, `M1036`, `M1032`, `M1027`, `M1037`, the `VULN-*` and `DOC-*` IDs from Task 6) plus any additional extracted nodes; every node has `origin: "extracted"` or `"curated"`; `CURATION.md` lists each change from raw to curated (renamed IDs, merged duplicates, removed/added links) so the report can cite it.

- [ ] **Step 1: Curate** raw → curated, keeping every ID listed above.
- [ ] **Step 2: Run** `dotnet test backend/tests/GraphService.Tests` — Expected: all pass (the seed/query tests load the real file).
- [ ] **Step 3: Verify** on the running stack: restart graph-service; `controls-for-alert` still returns the T1110 controls; `MATCH (n) WHERE n.origin IN ['extracted','curated'] RETURN n.origin, count(*)`.
- [ ] **Step 4: Commit** — `feat(graph): curated security knowledge from Graph Builder extraction`
