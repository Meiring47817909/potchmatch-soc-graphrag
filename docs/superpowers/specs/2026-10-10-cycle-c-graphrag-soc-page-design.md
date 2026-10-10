# Cycle C — GraphRAG, Rule 5 and the Admin SOC Page (Design)

**Project:** PotchMatch (ITRI623 Phase 2)
**Cycle:** C of 3 (A: events/rules/alerts ✅ → B: Neo4j knowledge graph ✅ → **C: GraphRAG + Rule 5 + admin SOC page**)
**Covers Phase 2 spec sections:** §3.3 Rule 5; §3.4 alerts displayed; §5 RAG over the knowledge graph (Option A, §5.3 evidence); §7 demo steps 9–10; §11.6 RAG demonstration
**Date:** 2026-10-10
**Status:** Approved in conversation, awaiting written-spec review

---

## 1. Goal and success criteria

Let an admin ask natural-language security questions that are answered from the Neo4j knowledge graph with visible evidence (Cypher-based GraphRAG, spec Option A); add detection Rule 5 (Suspicious Graph Query → GRAPH_ACCESS); and give admins an Angular SOC page where alerts and incidents are displayed and questions are asked.

Cycle C is done when:

1. An admin can ask a question (free text, or pre-filled from an alert/incident) and receives an answer generated only from graph evidence, together with: the Cypher that ran and its parameters, the retrieved nodes and relationships, the source documents/procedures, and an explanation of the retrieval method.
2. The full §7 workflow runs live: failed logins → alert → graph → "Why was this alert created and what should be done?" on the SOC page → grounded answer with evidence.
3. At least three spec questions (§11.6) work end to end, and the Postman "Phase 2 — RAG" folder reproduces them.
4. Rule 5 raises a HIGH `GRAPH_ACCESS` alert when a user (or IP without token) is denied 3+ times on sensitive graph/RAG endpoints within 5 minutes; the alert reaches the graph linked to OWASP A01 and its controls.
5. Generated Cypher can never modify the graph (guard + read transaction), and without a Gemini key the system still runs with the Ask feature clearly disabled.

## 2. Constraints

- Work continues in `squash-platform-phase2`; Phase 1 (`squash-platform`) is never touched.
- Same stack and patterns: .NET 10 Web API, Serilog → SOC, Consul registration, Ocelot gateway, Docker Compose, Angular 22 (standalone components, signals) served by nginx. One `docker compose up` still starts everything.
- **LLM:** Google Gemini via an AI Studio API key, called over HTTPS from the backend only. Key in `backend/.env` (`GEMINI_API_KEY`, git-ignored); model configurable (`GEMINI_MODEL`, default `gemini-2.5-flash`). No key is ever committed or sent to the browser.
- **No key → feature disabled** (no template fallback): `/api/rag/status` reports it, `/api/rag/ask` returns 503 with an explanatory message, the Ask box is disabled with instructions (set the key in `backend/.env`, then `docker compose up -d rag-service`; a plain `restart` keeps the old environment). Everything else works.
- GraphService remains the only component with Neo4j credentials (database-per-service).
- Admin-only: RAG and the SOC page are for the Admin role; players/coaches are denied at the gateway and in each service.

## 3. Out of scope

- Template-based fallback answers (Option C), vector search / chunks (Option B), multiple LLM providers.
- Streaming answers, chat history, follow-up questions.
- Changes to Rules 1, 3, 4 and to Cycle B's sync/incident logic (other than the additions in §6).

## 4. Architecture

```
Admin (Angular SOC view) ──► Gateway ──► /api/soc/*   SocService   (alerts, status changes, rules)
                                     ├─► /api/graph/* GraphService (incidents, named queries)
                                     └─► /api/rag/*   RagService ──► Gemini API (generate Cypher; generate answer)
                                                          │
                                                          └─► GraphService /api/internal/query + /api/internal/schema
                                                                   (key-protected, not routed) ──► Neo4j (read-only)
```

**Why RagService is separate from GraphService** (decided in brainstorming; for the report): single responsibility (GraphService already owns the graph, seeding, sync and incidents); failure isolation (Gemini latency/429/missing key can only affect Ask, never sync or the named queries, and graph-service health stays green); security boundary (the component handling untrusted LLM-generated Cypher has no Neo4j credentials and reaches the graph only through one guarded read-only endpoint); independent change of prompts/model.

### 4.1 New: RagService (`backend/src/RagService`, container `rag-service`)

- .NET 10 Web API; Serilog with `ServiceName = "rag-service"` shipping to SocService like the other services; Consul registration with health check; JWT bearer validation like the other services; `[Authorize(Roles = "Admin")]`.
- `GET /api/rag/status` → `{ enabled: bool, model: string|null, reason: string|null }` (`reason` = "GEMINI_API_KEY is not set" when disabled).
- `POST /api/rag/ask` → body `{ question: string (1..500 chars), alertId?: string, incidentId?: string }` → `RagAnswer` (§5.4).
- Clients: `IGeminiClient` (typed HttpClient to the Gemini `generateContent` REST API, JSON-mode responses, 25 s timeout, `Gemini:ThinkingBudget` default 0) and `IGraphQueryClient` (typed HttpClient to GraphService internal endpoints with header `X-Graph-Query-Key`).
- Schema cache: 10 minutes.

### 4.2 Changed: GraphService

- `POST /api/internal/query` (header `X-Graph-Query-Key`, constant-time compare, wrong/missing → 401; not routed through the gateway): body `{ cypher: string, parameters: object }` → existing `GraphQueryResult` shape `{ query: "rag", cypher, parameters, nodes, relationships, rows }` plus `truncated: bool`. Guard rejection → 400 `{ error: "rejected", reason }`; Neo4j syntax/runtime error → 400 `{ error: "cypher_error", reason }`; Neo4j unavailable → 503.
- **Cypher guard** (pure, unit-tested) runs before execution:
  - strips comments (`//…`, `/*…*/`) and string literals before keyword scanning;
  - exactly one statement (no `;` except a trailing one);
  - rejects any Unicode control/format character other than tab, CR, LF (Neo4j treats some as whitespace);
  - rejects (case-insensitive, as tokens) the write/admin clause keywords `CREATE`, `INSERT`, `MERGE`, `DELETE`, `DETACH`, `SET`, `REMOVE`, `DROP`, `LOAD` (CSV), `FOREACH`, `USE`, `SHOW`, `TERMINATE`, `GRANT`, `DENY`, `REVOKE`, `ALTER`, `START`, `STOP`, `RENAME`, `ENABLE`, `DEALLOCATE`, `REALLOCATE`, and the word `TRANSACTIONS` (covers `IN [CONCURRENT] TRANSACTIONS`);
  - qualified names are normalised (backticks, whitespace and comments around `.` removed) before checking; `CALL <procedure>` is allowed only for `db.labels`, `db.relationshipTypes`, `db.propertyKeys` (`CALL {…}` / `CALL (x) {…}` subqueries are scanned normally); qualified function calls are allowed only for `apoc.map.*`, `apoc.coll.*`, `apoc.text.*`, `apoc.convert.*` (except `apoc.convert.setJsonProperty`) and the read-only built-in namespaces `date.`, `datetime.`, `localdatetime.`, `localtime.`, `time.`, `duration.`, `point.`, `vector.`;
  - (rulings R3/R4 during implementation hardened the original keyword list after a review proved bypasses on Neo4j 5.26);
  - must contain `RETURN`; appends `LIMIT 200` when the statement has no `LIMIT`.
- Execution in a **read transaction** (`ExecuteReadAsync`) with a 10 s transaction timeout — Neo4j itself refuses writes even if the guard were bypassed. Result rows capped at 200 (`truncated: true` when hit).
- `GET /api/internal/schema` (same key): `{ labels: [{ label, keyProperty, properties[], count }], relationshipTypes: [{ type, from[], to[], count }], samples: { ruleNames[], threatIds[], severities[], alertStatuses[], incidentStatuses[], serviceNames[] } }` built from `GraphSchema` + live `MATCH` counts/samples.
- Rule→threat map gains `GRAPH_ACCESS → OWASP-A01-2025`.

### 4.3 Changed: SocService — Rule 5

- New rule `GraphAccessRule` (§6.1) registered with the other rules, documented in `/api/soc/rules`.
- Config `DetectionRules:GraphAccess { Threshold: 3, WindowMinutes: 5, SensitivePathPrefixes: ["/api/graph/", "/api/rag/"] }`.
- Rule 2 (`UnauthorisedEndpointAccessRule`) ignores events whose endpoint starts with a sensitive prefix, so one denied request feeds exactly one rule. Sensitive-prefix matching is case-insensitive and shared via one helper.
- Gateway is **unchanged**: it keeps emitting `UNAUTHORISED_ACCESS` (with `endpoint`, `userId` if a token was present, `sourceIp`, `statusCode`) for its own 401/403 responses.

### 4.4 Changed: Gateway, Compose, seeds

- Ocelot route `/api/rag/{everything}` → `rag-service`, methods GET/POST, same `AuthenticationOptions`, `RouteClaimsRequirement { "role": "Admin" }`, X-Forwarded-For like the others.
- Compose: `rag-service` (build `./src/RagService`, depends on consul + graph-service, env `Gemini__ApiKey=${GEMINI_API_KEY:-}`, `Gemini__Model=${GEMINI_MODEL:-gemini-2.5-flash}`, `GraphQuery__Url=http://graph-service:8080`, `GraphQuery__Key`, Consul/ServiceRegistration/Serilog-to-SOC vars like other services); `GraphQuery__Key` (dev value `dev-graph-query-key-change-me`) also on graph-service. `backend/.env.example` documents `GEMINI_API_KEY=` and `GEMINI_MODEL=`; `backend/.env` is git-ignored.
- `graph-data/app-structure.json`: Service `rag-service` (kind `application`); Endpoints `POST /api/rag/ask`, `GET /api/rag/status` (protected, `REQUIRES_ROLE` Admin, `EXPOSES` from rag-service); `api-gateway DEPENDS_ON rag-service`; `rag-service DEPENDS_ON graph-service`; `rag-service USES` the external Gemini API is **not** modelled as an Asset (YAGNI); Threat stub `OWASP-A01-2025` with `TARGETS` → `graph-service`, `rag-service`.
- `graph-data/knowledge/curate.py` adds three curated edges, justified in `CURATION.md` from OWASP A01:2025 "How to Prevent": `M1018 —MITIGATES→ OWASP-A01-2025` (least privilege / deny by default), `CTRL-SESSION-REVOCATION —MITIGATES→ OWASP-A01-2025` (invalidate sessions/tokens), `DOC-SESSION-REVOCATION —SUPPORTS_RESPONSE_TO→ OWASP-A01-2025`. Regenerate `security-knowledge.json`.
- `graph-data/README.md`: the "changing a seed" note recommends removing only the `neo4j-data` volume (`docker compose stop graph-service neo4j; docker compose rm -f neo4j; docker volume rm squash-platform-phase2_neo4j-data`), not `down -v` (which also wipes Postgres data).

## 5. RAG pipeline (one question)

### 5.1 Steps

1. **Schema** — RagService gets the schema (§4.2) from cache or GraphService.
2. **Generate Cypher** — Gemini prompt contains: role + rules ("read-only Cypher only", "use parameters `$alertId`/`$incidentId` when provided, never inline IDs", "return paths (`p = …`) so relationships are included", "follow Alert→Threat→Control and Threat→Document links for 'what should be done' questions", "prefer Documents via SUPPORTS_RESPONSE_TO/DESCRIBES for procedures"), the schema, ~7 few-shot examples (the existing named queries: alerts-affecting-service, users-high-severity, threats-for-service, controls-for-alert, dependency-impact, incidents, incident-detail, each as question → Cypher), the provided context IDs, and the question. Response JSON: `{ "cypher": string, "parameters": object, "explanation": string }`. Provided `alertId`/`incidentId` are merged into parameters by RagService (the model cannot override them).
3. **Execute** — `POST /api/internal/query`. On 400 (`rejected` or `cypher_error`) RagService makes **one repair attempt**: the error reason and the failed Cypher go back to Gemini with the same context; the repaired query is executed once. If it fails again → answer "I could not build a valid graph query for this question." with the failing Cypher and reason in the evidence; HTTP 200, `answered: false`.
4. **No evidence** — 0 rows and 0 nodes → answer "The knowledge graph has no evidence for this question." with the Cypher; Gemini is not called for an answer; `answered: false`.
5. **Generate answer** — Gemini prompt contains only the question and the evidence: up to 50 rows (JSON) plus readable triples built from relationships (`<start display> —TYPE→ <end display>`, display = name/title/id/key). Rules: answer only from the evidence; cite node identifiers in brackets (e.g. `[T1110]`, `[DOC-PASSWORD-RESET]`); state explicitly when the evidence does not cover part of the question; 3–8 sentences. Response JSON: `{ "answer": string }`.
6. **Respond** — `RagAnswer` (§5.4).

### 5.2 Errors

| Condition | Result |
|---|---|
| No key | 503 `{ error: "rag_disabled", message: "GEMINI_API_KEY is not set …" }` |
| Question empty or > 500 chars | 400 |
| Gemini 429 / 5xx / timeout / unparseable JSON | 503 `{ error: "llm_unavailable", message: "The language model is busy or unavailable; try again in a minute." }` |
| GraphService unreachable / 503 | 503 `{ error: "graph_unavailable" }` |
| Guard/Cypher error after repair | 200 with `answered: false` (§5.1 step 3) |

### 5.3 Safety

- Prompt injection in the question ("delete everything") cannot change data: guard + read transaction + no Neo4j credentials in RagService.
- Model output is only displayed (Angular text binding, never `innerHTML`) or executed through the guard.
- Evidence sent to Gemini is capped at 50 rows; the response says when evidence was truncated.
- RagService logs question length, mode, attempts, row count and latency (not the full question text) to the SOC pipeline.

### 5.4 Response shape (`RagAnswer`, camelCase JSON)

```json
{
  "question": "Why was this alert created and what should be done?",
  "answered": true,
  "answer": "… [alert-…] … [T1110] … [M1032] … [DOC-PASSWORD-RESET] …",
  "mode": "text2cypher",
  "retrieval": {
    "method": "LLM-generated Cypher, validated read-only, executed on Neo4j",
    "model": "gemini-2.5-flash",
    "attempts": 1,
    "explanation": "Follows the alert to the threat it indicates, the controls that mitigate it and the procedures that respond to it.",
    "rowCount": 6,
    "truncated": false
  },
  "cypher": "MATCH p = …",
  "parameters": { "alertId": "…" },
  "nodes": [ { "id": "…", "label": "Threat", "properties": { … } } ],
  "relationships": [ { "id": "…", "type": "INDICATES", "start": "…", "end": "…" } ],
  "rows": [ { … } ],
  "sources": [ { "id": "DOC-PASSWORD-RESET", "name": "Password Reset Procedure", "url": null },
               { "id": "DOC-T1110", "name": "MITRE ATT&CK T1110 Brute Force", "url": "https://attack.mitre.org/techniques/T1110/" } ]
}
```

`sources` = every `Document` node among the evidence nodes. No numeric confidence score is produced; the spec's alternative ("explanation of retrieval method") is satisfied by `retrieval`.

## 6. Rule 5 — Suspicious Graph Query

### 6.1 Rule documentation (served by `/api/soc/rules`)

| Field | Value |
|---|---|
| Rule name | `GRAPH_ACCESS` (spec name: Suspicious Graph Query) |
| Purpose | Detect someone probing the knowledge graph or RAG interface without authorisation (reconnaissance of users, dependencies, incidents) |
| Input events | `UNAUTHORISED_ACCESS` events whose `endpoint` starts with a sensitive prefix (`/api/graph/`, `/api/rag/`) |
| Detection condition | ≥ 3 such events within 5 minutes for the same user (`userId`), or for the same `sourceIp` when no user is known |
| Severity | HIGH |
| Alert generated | `GRAPH_ACCESS` alert: description names the user/IP and count; `affectedService` = `graph-service` or `rag-service` (most targeted; ties → `graph-service`); `affectedUser` = userId (if any); `affectedEntity` = last endpoint tried; linked to the events |
| Recommended response | Review the account's role and recent activity; revoke its sessions and block the source IP if the attempts were not expected |

Dedup keys follow the Rule 2 pattern (`GRAPH_ACCESS:user:{id}` / `GRAPH_ACCESS:ip:{ip}`); existing alert dedup, event linking and RESOLVED-exclusion behaviour applies unchanged.

### 6.2 Graph effect

Synced GRAPH_ACCESS alerts get `INDICATES → OWASP-A01-2025` and `AFFECTS → graph-service|rag-service`; incident correlation is unchanged (a user who also triggers Rule 2 by probing `/api/soc/*` within 30 minutes produces an escalated incident).

## 7. Admin SOC page (Angular)

- Top bar: a **SOC** button visible only when `currentPlayer().role === 'Admin'`; after an admin logs in the SOC view is shown by default. View switching stays signal-based (`view: 'map' | 'dashboard' | 'soc'`).
- Components under `src/app/soc/`: `soc-page` (layout), `alerts-panel`, `incidents-panel`, `ask-panel`, `evidence-panel`, `graph-view`; services `soc-api` (alerts, status PATCH, rules), `graph-api` (incidents), `rag-api` (status, ask). Existing auth interceptor supplies the token; API base from `core/api-config.ts`.
- **Alerts panel**: table from `GET /api/soc/alerts` with status/severity/rule filters; columns time, rule, severity, affected user, service, status; actions Acknowledge / Resolve (`PATCH /api/soc/alerts/{id}/status`), and **Why?** (fills the Ask box with "Why was this alert created and what should be done?" and sets `alertId`). Refresh button + 30 s auto-refresh.
- **Incidents panel**: `GET /api/graph/incidents`; columns title, subject, priority, escalated badge, alert count, last alert; **Ask** fills "What does the graph know about this incident, its affected services and possible response actions?" and sets `incidentId`.
- **Ask panel**: textarea (max 500), example-question chips (spec §5.1 list), Ask button; disabled with the status `reason` + "Set GEMINI_API_KEY in backend/.env and run `docker compose up -d rag-service` (this recreates the container)" when `enabled` is false; loading and error states (503 messages shown verbatim).
- **Evidence panel**: answer text; retrieval-method line (method, model, attempts, rows, truncated) + explanation; **graph view** (Cytoscape.js, nodes coloured by label, labels = display name, relationship type on edges, `cose` layout, fit to container); relationships list as readable lines; Cypher + parameters in a monospace block; sources list with links (`url`) or IDs.
- New dependency: `cytoscape` (+ `@types/cytoscape`), pinned versions.

## 8. Testing

- **RagService.Tests (xUnit, fakes for Gemini and graph clients):** happy path (generate → execute → answer, sources from Document nodes, context IDs merged and not overridable); repair after `rejected` and after `cypher_error`; second failure → `answered: false`; zero rows → no-evidence answer and answer-model not called; no key → status disabled + ask 503; Gemini 429/timeout/bad JSON → 503 `llm_unavailable`; graph 503 → 503; question validation; evidence truncation to 50 rows reported.
- **GraphService.Tests:** CypherGuard unit tests (each forbidden clause; lowercase/mixed case; keywords inside comments and string literals ignored; multi-statement rejected; trailing `;` allowed; `LIMIT` appended only when missing; apoc allow-list; `RETURN` required); internal query endpoint key check; Testcontainers integration: a write statement executed through the read path is refused by Neo4j even with the guard bypassed; schema endpoint returns labels/types/samples; `GRAPH_ACCESS` maps to `OWASP-A01-2025`; seed tests updated for rag-service, OWASP stub and the three curated edges.
- **SocService.Tests:** Rule 5 fires at 3 sensitive denials in 5 min (not 2, not outside the window), groups by user else IP, picks affectedService correctly; Rule 2 ignores sensitive endpoints but still fires for others; rules endpoint lists GRAPH_ACCESS with all documentation fields.
- **Angular:** evidence mapping (nodes/relationships → readable lines and Cytoscape elements); Ask disabled state; SOC button only for Admin.
- **Postman "Phase 2 — RAG":** RAG status; three spec questions (why was this alert created and what should be done; what controls can mitigate this threat; what is the likely impact if payment-service fails); Rule 5 probe (player token ×3 on `/api/graph/incidents` → GRAPH_ACCESS alert visible in `/api/soc/alerts`).
- **End-to-end (on the user's PC, after implementation):** the §7 workflow and the Rule 5 + Rule 2 escalation scenario.

## 9. Report notes (for the user's write-up)

- Retrieval process = §5.1 steps; the evidence panel shows the real Cypher, parameters and attempts for every answer (spec §5.3, §11.6 "answer came from Cypher").
- Design choices: separate RagService (§4); event types describe what happened, rules filter on attributes (Rule 5 uses `UNAUTHORISED_ACCESS` + sensitive-path filter, Rule 2 excludes those paths); guard + read transaction as defence in depth.
- Limitations: depends on an external LLM and key (disabled without one); generated Cypher quality varies with the question (mitigated by schema, few-shot examples and one repair attempt); free-tier rate limits.
