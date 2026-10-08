# Cycle B — Neo4j Security Knowledge Graph (Design)

**Project:** PotchMatch (ITRI623 Phase 2)
**Cycle:** B of 3 (A: events/rules/alerts ✅ → **B: Neo4j knowledge graph** → C: GraphRAG + admin SOC page)
**Covers Phase 2 spec sections:** §4 Knowledge Graph (4.1 node types, 4.2 relationships, 4.3 three data sources, 4.4 five queries); §7 demo workflow steps 6–8; §11.3 graph model deliverable
**Date:** 2026-10-08
**Status:** Approved in conversation, awaiting written-spec review

---

## 1. Goal and success criteria

Build a local Neo4j security knowledge graph that connects PotchMatch's application structure, the events and alerts produced by Cycle A, and formal security knowledge (MITRE ATT&CK, OWASP, our own response procedures), and expose at least five named graph queries.

Cycle B is done when:

1. Neo4j runs as part of `docker compose up` and contains all 12 required node types (§3) and all 14 spec relationship types, loaded from three sources: app structure, live SOC data, security knowledge.
2. The §7 workflow steps 6–8 work: a Cycle A alert appears in Neo4j within ~30 s, linked to the user, service, endpoint and threat; the threat links to the controls that mitigate it and the documents/procedures that support the response.
3. Related alerts are grouped into Incidents, with escalation when an incident spans two or more rules.
4. The five queries (§6) return correct results through admin-only API endpoints, each response including the Cypher used and the retrieved nodes/relationships.
5. Restarting GraphService, or re-running a sync, never duplicates anything.

## 2. Constraints

- Work continues in `squash-platform-phase2`; Phase 1 (`squash-platform`) is never touched.
- Same stack: .NET 10 Web API, Serilog, Consul registration, Ocelot gateway, Docker Compose. Everything still starts with one `docker compose up`.
- **Neo4j runs locally** (`neo4j:5-community` container). The running system never depends on Aura or internet access.
- Database-per-service: GraphService alone owns Neo4j. It reads SocService and IdentityService only through their APIs, never their databases.
- Aura + the Neo4j LLM Graph Builder are used **only as an offline extraction tool** for the security-knowledge seed (§5.3). Document ingestion is not demonstrated and Graph Builder's chunks/embeddings are not carried over.

## 3. Out of scope for Cycle B

- The natural-language query interface, LLM/RAG, and the admin SOC page in Angular → Cycle C.
- Phase 2 spec Rule 5 (Suspicious Graph Query) → Cycle C, where the query interface lives.
- Vector indexes / embeddings (Option B RAG was not chosen).
- Changing Cycle A's detection rules or alert logic.

## 4. Graph model

### 4.1 Node types

| Label | Key (unique constraint) | Main properties | Source |
|---|---|---|---|
| Service | `name` | `kind` (`gateway` / `domain` / `soc` / `graph`) | app structure |
| Endpoint | `key` = `"{METHOD} {pathTemplate}"` | `method`, `path` (template, e.g. `/api/players/{id}`), `protected` (bool), `discovered` (bool, default false) | app structure (+ discovered from events) |
| Asset | `name` | `type` (`database` / `registry`) | app structure |
| Role | `name` | `Player`, `Coach`, `Admin` | app structure |
| User | `userId` | `email`, `name`, `unknownAccount` (bool) | IdentityService players + events |
| Event | `eventId` | `timestamp`, `eventType`, `severity`, `message`, `statusCode`, `sourceIp`, `correlationId`, `affectedEntity` | SocService |
| Alert | `alertId` | `timestamp`, `ruleName`, `severity`, `status`, `description`, `recommendedAction` | SocService |
| Incident | `incidentId` | `title`, `subject`, `severity`, `status`, `escalated`, `priority`, `openedAt`, `lastAlertAt` | GraphService correlation (§7) |
| Threat | `id` (e.g. `T1110`) | `name`, `description`, `source` | knowledge seed |
| Control | `id` (e.g. `M1036`) | `name`, `description`, `source` | knowledge seed |
| Vulnerability | `id` | `name`, `description` | knowledge seed |
| Document | `id` | `title`, `kind` (`MITRE` / `OWASP` / `Procedure`), `url` | knowledge seed |

Plus one bookkeeping label, `SyncState {name, cursor}`, which is not part of the security model.

### 4.2 Relationships

All 14 from the Phase 2 spec:

`(:User)-[:TRIGGERED]->(:Event)`, `(:Event)-[:GENERATED_BY]->(:Service)`, `(:Event)-[:TARGETED]->(:Endpoint)`, `(:Event)-[:CREATED_ALERT]->(:Alert)`, `(:Alert)-[:INDICATES]->(:Threat)`, `(:Threat)-[:TARGETS]->(:Service)`, `(:Threat)-[:EXPLOITS]->(:Vulnerability)`, `(:Control)-[:MITIGATES]->(:Threat)`, `(:Incident)-[:CONTAINS]->(:Alert)`, `(:Service)-[:DEPENDS_ON]->(:Service)`, `(:Service)-[:EXPOSES]->(:Endpoint)`, `(:User)-[:HAS_ROLE]->(:Role)`, `(:Document)-[:DESCRIBES]->(:Control)`, `(:Document)-[:SUPPORTS_RESPONSE_TO]->(:Threat)`.

Three additions, each used by a query:

- `(:Alert)-[:AFFECTS]->(:Service)` — query 1;
- `(:Service)-[:USES]->(:Asset)` — places Asset nodes in the graph;
- `(:Endpoint)-[:REQUIRES_ROLE]->(:Role)` — e.g. `/api/soc/*` and `/api/graph/*` require Admin.

### 4.3 How live data attaches

- **Endpoint normalisation:** path segments that are GUIDs or integers become `{id}` (e.g. `/api/players/5e9a…` → `/api/players/{id}`); the query string is dropped. The event links to `Endpoint {key: "{METHOD} {template}"}`. If no such endpoint exists in the app-structure seed, one is created with `discovered: true`. Events with no endpoint (e.g. health transitions) get no `TARGETED` relationship.
- **Event → Service:** `GENERATED_BY` uses the event's `serviceName`.
- **User linkage:** `userId` on an event is either a player GUID or, for `FAILED_LOGIN`, the attempted email. A GUID matches `User.userId`. An email matches `User.email` case-insensitively (trimmed, lower-cased); if no account has that email, a User is created with `userId = "email:{email}"`, `email`, `unknownAccount: true`. Events with no userId get no `TRIGGERED`.
- **Alert → Threat** (fixed mapping in GraphService config):

| ruleName | Threat |
|---|---|
| `Multiple Failed Logins` | `T1110` Brute Force |
| `Unauthorised Endpoint Access` | `T1078` Valid Accounts |
| `POSSIBLE_DOS` | `T1499` Endpoint Denial of Service |
| `SERVICE_FAILURE` | `T1499` Endpoint Denial of Service |

- **Alert → Service:** `AFFECTS` uses the alert's `affectedService`.
- **User roles:** `HAS_ROLE` from the IdentityService player list; a user's previous `HAS_ROLE` is replaced if their role changes.

## 5. Data sources

### 5.1 App-structure seed — `graph-data/app-structure.json`

Hand-written and committed. Contains:

- **Services:** `api-gateway` (gateway), `identity-service`, `booking-service`, `payment-service` (domain), `soc-service` (soc), `graph-service` (graph).
- **Endpoints:** every route of the current controllers and the gateway (`/api/auth/register`, `/api/auth/login`, `/api/players`, `/api/players/{id}`, `/api/players/{id}/password`, `/api/bookings`, `/api/bookings/{id}`, `/api/bookings/{id}/accept`, `/api/payments/{id}`, `/api/payments/by-booking/{id}`, `/api/payments/wallet`, `/api/payments/wallet/deposit`, `/api/payments/hold`, `/api/payments/{id}/release`, `/api/payments/{id}/refund`, `/api/soc/alerts`, `/api/soc/alerts/{id}`, `/api/soc/alerts/{id}/status`, `/api/soc/events`, `/api/soc/rules`, and the `/api/graph/*` endpoints of §8), each with method, `protected`, and owning service (`EXPOSES`).
- **Assets:** `identity_db`, `booking_db`, `payment_db`, `soc_db`, `neo4j`, `consul`, with `USES` from their owners (every service `USES` consul).
- **Roles:** Player, Coach, Admin; `REQUIRES_ROLE` Admin on the `/api/soc/*` and `/api/graph/*` endpoints.
- **Dependencies:** `api-gateway DEPENDS_ON` identity, booking, payment, soc and graph services; `booking-service DEPENDS_ON payment-service`; `graph-service DEPENDS_ON soc-service` and `identity-service`; `soc-service DEPENDS_ON consul` is expressed via `USES`.
- **Threat targets:** `T1110 TARGETS identity-service`; `T1078 TARGETS api-gateway, identity-service, soc-service, graph-service`; `T1499 TARGETS api-gateway, identity-service, booking-service, payment-service`.

### 5.2 Live SOC and identity data

New internal endpoints, following Cycle A's ingest pattern: **not routed through the gateway**, protected by a shared key header `X-Graph-Sync-Key` (config `GraphSync:Key`), compared in constant time; wrong/missing key → 401.

| Endpoint | Service | Behaviour |
|---|---|---|
| `GET /api/internal/sync/alerts?updatedSince={ts}&limit={n}` | SocService | Alerts with `updated_at > updatedSince` (all if omitted), ordered by `updated_at` then `alert_id`, at most `limit` (default 200, max 500). Each alert in the Phase 2 spec alert shape plus `updatedAt`, with `relatedEvents` expanded to **full event objects** (all 13 spec fields). |
| `GET /api/internal/sync/events?receivedAfter={ts}&limit={n}` | SocService | Events with `received_at > receivedAfter`, **excluding `REQUEST_RECEIVED`** and excluding events linked to any alert, ordered by `received_at` then `event_id`, at most `limit` (default 500, max 1000). Each includes `receivedAt`. |
| `GET /api/internal/sync/players` | IdentityService | All players: `id`, `email`, `name`, `surname`, `role`. |

`REQUEST_RECEIVED` events enter the graph only as part of an alert (e.g. POSSIBLE_DOS evidence), keeping routine traffic out of the graph.

### 5.3 Security-knowledge seed — Graph Builder pipeline

1. **Sources** in `graph-data/knowledge/sources/` (plain text / markdown, with source URL and licence/attribution in each file's header):
   - MITRE ATT&CK techniques: T1110 Brute Force (incl. sub-techniques .001 Password Guessing, .003 Password Spraying, .004 Credential Stuffing), T1078 Valid Accounts, T1499 Endpoint Denial of Service.
   - MITRE mitigations: M1036 Account Use Policies, M1032 Multi-factor Authentication, M1027 Password Policies, M1037 Filter Network Traffic.
   - OWASP Top 10 2021 A01 Broken Access Control.
   - Four procedures written for PotchMatch: Password Reset Procedure, Session Revocation Procedure, IP Blocking Procedure, Service Outage Runbook.
2. **Extraction** (performed by the user in the Neo4j LLM Graph Builder against Aura), with the schema restricted to node labels `Threat`, `Control`, `Vulnerability`, `Document` and relationship types `MITIGATES`, `EXPLOITS`, `DESCRIBES`, `SUPPORTS_RESPONSE_TO`. Step-by-step instructions live in `graph-data/knowledge/README.md`.
3. **Export:** the user runs `graph-data/knowledge/export.cypher` in Aura's query tool; it returns only those four labels and four relationship types as JSON (no `Chunk` nodes, no embeddings). Saved as `graph-data/knowledge/security-knowledge.raw.json`.
4. **Curation** into `graph-data/knowledge/security-knowledge.json`: stable IDs (`T1110`, `M1036`, `DOC-PASSWORD-RESET`, …), near-duplicates merged, wrong links fixed, each node tagged `origin: "extracted"` or `origin: "curated"`. Gaps from weak extraction are filled by hand and tagged `curated`.
5. GraphService loads only `security-knowledge.json`. The raw export and sources remain in the repo as provenance.

**Seed file format** (both seeds): `{ "nodes": [{ "label", "key", "properties" }], "relationships": [{ "type", "from": {label, key}, "to": {label, key} }] }`. The loader rejects unknown labels/relationship types and relationships whose endpoints aren't in the file or already-loaded seeds.

## 6. The five required queries

Each is a named, parameterised Cypher query in GraphService with an integration test.

| # | Name | Endpoint | Traversal |
|---|---|---|---|
| 1 | `alerts-affecting-service` | `GET /api/graph/services/{name}/alerts` | `(a:Alert)-[:AFFECTS]->(s:Service {name})`, newest first |
| 2 | `users-high-severity` | `GET /api/graph/users/high-severity` | `(u:User)-[:TRIGGERED]->(:Event)-[:CREATED_ALERT]->(a:Alert {severity:'HIGH'})`, with alert count per user |
| 3 | `threats-for-service` | `GET /api/graph/services/{name}/threats` | `(t:Threat)-[:TARGETS]->(s)` ∪ threats indicated by alerts affecting `s` |
| 4 | `controls-for-alert` | `GET /api/graph/alerts/{id}/controls` | `(a:Alert)-[:INDICATES]->(t:Threat)<-[:MITIGATES]-(c:Control)`, plus `(d:Document)-[:DESCRIBES]->(c)` and `(d:Document)-[:SUPPORTS_RESPONSE_TO]->(t)` |
| 5 | `dependency-impact` | `GET /api/graph/services/{name}/impact` | `(dep:Service)-[:DEPENDS_ON*1..]->(s)`, with path length |

Plus `GET /api/graph/incidents?status=` and `GET /api/graph/incidents/{id}` (incident with its alerts, users, services and threats).

**Response shape (all graph endpoints):**

```json
{
  "query": "controls-for-alert",
  "cypher": "MATCH ...",
  "parameters": { "alertId": "..." },
  "nodes": [{ "id": "<elementId>", "label": "Control", "properties": { } }],
  "relationships": [{ "id": "<elementId>", "type": "MITIGATES", "start": "<elementId>", "end": "<elementId>" }],
  "rows": [ { } ]
}
```

Nodes and relationships are de-duplicated. `rows` is a flat, table-friendly view for the report and Postman.

## 7. Incident correlation

Runs after each sync cycle.

- **Subject** of an alert: `user:{userId}` if it has an affected user — `affectedUser` resolved to a User node with the §4.3 matching rules, or else a user who TRIGGERED one of its events — else `ip:{sourceIp}` from its events (most frequent), else `service:{affectedService}`.
- **Grouping:** alerts are processed in timestamp order. An alert joins the incident with the same subject whose `lastAlertAt` is within **30 minutes** of the alert's timestamp and whose status is not RESOLVED; otherwise it opens a new incident. An alert already contained in an incident is never moved.
- **Incident ID:** deterministic — `INC-` + first 12 hex chars of SHA-256(`subject` + first alert's `alertId`).
- **Derived properties:** `severity` = highest severity of contained alerts; `status` = `OPEN` if any contained alert is OPEN or ACKNOWLEDGED, else `RESOLVED`; `title` = `"Activity from {subject display}: {distinct rule names, comma-separated}"`; `escalated` = true and `priority` = `HIGH` when the incident contains alerts from ≥2 distinct rules, otherwise `escalated` = false and `priority` = the incident severity.

The 30-minute window is configurable (`Incidents:WindowMinutes`).

## 8. GraphService

- .NET 10 Web API in `backend/src/GraphService`, official `Neo4j.Driver`, Serilog, Consul registration as `graph-service`, JWT validation with `[Authorize(Roles = "Admin")]` on all `/api/graph/*` endpoints (same pattern as SocService).
- **Startup:** connect to Neo4j with retry/backoff; create uniqueness constraints for every key in §4.1; load `app-structure.json` then `security-knowledge.json` with idempotent `MERGE`.
- **Sync worker** (every 15 s, configurable `GraphSync:IntervalSeconds`): players (every 60 s, configurable `GraphSync:PlayersIntervalSeconds`) → alerts with events → unlinked security events → incident correlation. Cursors in `SyncState` nodes (`alerts`, `events`).
- **Gateway:** new route `/api/graph/{everything}` → `graph-service`, JWT + `RouteClaimsRequirement {"role":"Admin"}`, `X-Forwarded-For` transform, methods GET only.

## 9. Docker Compose changes

- `neo4j`: image `neo4j:5-community`, ports `7474:7474` and `7687:7687`, `NEO4J_AUTH=neo4j/<dev password>`, volume `neo4j-data`, healthcheck.
- `graph-service`: depends on healthy `neo4j` and `consul`; env `Neo4j__Uri=bolt://neo4j:7687`, `Neo4j__User`, `Neo4j__Password`, `GraphSync__Key`, `GraphSync__SocUrl=http://soc-service:8080`, `GraphSync__IdentityUrl=http://identity-service:8080`, Consul and Jwt settings as the other services.
- `GraphSync__Key` also added to `soc-service` and `identity-service`.
- Gateway `ocelot.json` gains the `/api/graph/*` route.

## 10. Error handling

| Situation | Behaviour |
|---|---|
| Neo4j unavailable at startup | Retry with backoff; constraints and seeds applied once connected; sync starts after seeding. |
| SocService / IdentityService unreachable or non-2xx | Logged; cursor unchanged; retried next cycle. |
| One alert/event fails to merge | Processed in order, one transaction per alert (events in batches); cursor advances only past items stored; the failed item is retried next cycle. |
| Seed file malformed / unknown label / dangling relationship | Fail fast at startup with a clear error naming the file and item. |
| Alert with an unmapped ruleName | Loaded without `INDICATES`; warning logged. |
| Event endpoint not in the app-structure seed | `Endpoint` created with `discovered: true`. |
| Graph query for unknown service/alert/incident | 404. |
| Neo4j unavailable during a query | 503. |
| Wrong/missing `X-Graph-Sync-Key` | 401 from the internal sync endpoints. |

## 11. Testing

- **Unit (no database):** endpoint normalisation; rule→threat mapping; email/user matching; subject derivation; incident correlation (window boundary, resolved incidents not reused, escalation at ≥2 rules, deterministic IDs); seed-file validation (unknown label, dangling relationship).
- **Integration (real Neo4j via Testcontainers):** each of the 5 queries + incidents endpoint against a fixture graph; sync idempotency (same input twice → identical node and relationship counts); seed loading creates the expected counts; constraints exist.
- **Cross-service:** SocService and IdentityService sync endpoints — key validation, filtering (`updatedSince`, `receivedAfter`, `REQUEST_RECEIVED` and alert-linked exclusion), ordering, limit caps.
- **End to end** (new Postman folder "Phase 2 — Graph"): 5 failed logins → within ~30 s the user appears in `users-high-severity`; `controls-for-alert` for that alert returns T1110 with M1036, M1032, M1027 and the Password Reset Procedure; Neo4j Browser shows the chain; 3 unauthorised requests from the same IP → incident escalated.
- **Knowledge fixture:** until the user's Graph Builder export is curated, GraphService and its tests use a small hand-written fixture with the same format; the curated file replaces it in a dedicated plan step.

## 12. Handover to Cycle C

Cycle C's RAG calls GraphService's query endpoints (and may add a context endpoint) and displays the returned `cypher`, `nodes` and `relationships` as evidence. Rule 5 (suspicious graph queries) will hook into GraphService's request logging.
