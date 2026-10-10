# Cycle C — GraphRAG, Rule 5 and Admin SOC Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admins ask natural-language security questions answered from the Neo4j graph with visible evidence (Option A text2cypher via Gemini), Rule 5 raises GRAPH_ACCESS alerts, and an Angular SOC page displays alerts, incidents, answers and evidence.

**Architecture:** A new .NET `rag-service` calls Gemini to write Cypher, sends it to a new key-protected GraphService endpoint that guards it and runs it in a read transaction, then asks Gemini for an answer grounded only in the returned evidence. SocService gains Rule 5 (sensitive-path filter over existing access-denied events); the Angular app gains an Admin-only SOC view with a Cytoscape evidence graph.

**Tech Stack:** .NET 10, ASP.NET Core, Neo4j.Driver 6.3.0, Serilog, Consul 1.8.0, Ocelot 24.1.0, xUnit, Testcontainers.Neo4j 4.16.0, Gemini `generateContent` REST API, Angular 22 (standalone, signals, vitest), Cytoscape.js, Docker Compose, Postman.

**Spec:** `docs/superpowers/specs/2026-10-10-cycle-c-graphrag-soc-page-design.md`

## Global Constraints

- Work only in `squash-platform-phase2`; `squash-platform` is never modified. Per-service duplication convention (no shared library). Existing package versions unchanged; new backend packages only those already used elsewhere in the repo (JwtBearer 10.0.11, Serilog.AspNetCore 10.0.0, Consul 1.8.0, Serilog.Sinks.Http 9.2.1 — copy from SocService/GraphService csproj).
- Gemini: key from config `Gemini:ApiKey` (env `Gemini__ApiKey=${GEMINI_API_KEY:-}`), model `Gemini:Model` default `gemini-2.5-flash`, base URL `Gemini:BaseUrl` default `https://generativelanguage.googleapis.com/v1beta/`, timeout 30 s; key sent as header `x-goog-api-key`, never logged, never sent to the browser; `backend/.env` git-ignored, `backend/.env.example` committed.
- Internal graph endpoints: header `X-Graph-Query-Key`, config `GraphQuery:Key` (dev value `dev-graph-query-key-change-me`) on graph-service and rag-service; constant-time compare; wrong/missing/empty → 401; never routed through the gateway.
- Guarded execution: read transaction, 10 s timeout, max 200 rows (`truncated`), `LIMIT 200` appended when absent.
- RAG limits: question 1..500 chars; evidence to the answer prompt ≤ 50 rows; schema cache 10 min; exactly one repair attempt.
- Rule 5: rule name `GRAPH_ACCESS`, HIGH, threshold 3, window 5 min, `SensitivePathPrefixes` `["/api/graph/", "/api/rag/"]`; Rule→threat `GRAPH_ACCESS`→`OWASP-A01-2025`.
- Error JSON bodies exactly: `{ "error": "rag_disabled" | "llm_unavailable" | "graph_unavailable" | "rejected" | "cypher_error", "message" or "reason": string }` as in spec §4.2/§5.2.
- RagAnswer JSON exactly spec §5.4 (camelCase), plus `retrieval.unverifiedCitations` (plan refinement, see below).
- Admin only: `[Authorize(Roles = "Admin")]` in rag-service; gateway `RouteClaimsRequirement { "role": "Admin" }`.
- Preserve each modified file's existing line endings (several repo files are CRLF).
- Commit trailers:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_019wuPNJ5g7LKK4idzGvACog
  ```

**Plan refinements over the spec:**
1. Rule 5 input types mirror Rule 2 (`UNAUTHORISED_ACCESS` **and** `AUTH_TOKEN_REJECTED`), filtered to sensitive paths — a probe with a forged token must count too.
2. The few-shot examples come from GraphService (`/api/internal/schema` returns `examples: [{ name, question, cypher }]` built from `GraphQueries`), so RagService does not duplicate the Cypher.
3. `retrieval.unverifiedCitations: string[]` lists bracketed IDs in the answer that do not match any evidence node — makes the grounding check visible.

## Review Focus

1. **Gemini wraps JSON in ```json fences or adds prose** → still parsed → Task 5 `GeminiJson_FencedOrPrefixed_Parsed`.
2. **Ordinary Cypher containing keyword-like text** (`createdAt`, `n.offset`, `'Password Reset'`, `CONTAINS 'set'`) must not be rejected → Task 2 `Guard_KeywordsInsideIdentifiersOrStrings_Allowed`.
3. **Paths with different case or the bare prefix** (`/API/Graph/incidents`, `/api/rag`) count as sensitive → Task 1 `SensitivePaths_CaseAndBarePrefix`.
4. **Answer cites an ID not present in the evidence** → reported in `retrieval.unverifiedCitations`, not silently trusted → Task 6 `Answer_UnknownCitation_ReportedAsUnverified`.
5. **Admin's token expires while the SOC page is open** → panels show "Session expired — please log in again", not a blank table → Task 8 `AlertsPanel_401_ShowsSessionExpired`.

---

## File Structure

**New**
- `backend/src/SocService/Detection/Rules/GraphAccessRule.cs`, `backend/src/SocService/Detection/SensitivePaths.cs`
- `backend/src/GraphService/Rag/{CypherGuard.cs, GuardedQueryRunner.cs, GraphSchemaDescriber.cs, QueryExamples.cs}`, `backend/src/GraphService/Controllers/InternalQueryController.cs`
- `backend/src/RagService/` — `RagService.csproj`, `Dockerfile`, `.dockerignore`, `appsettings.json`, `Program.cs`; `Options/{GeminiOptions,GraphQueryOptions}.cs`; `Clients/{IGeminiClient,GeminiClient,IGraphQueryClient,GraphQueryClient,GraphDtos}.cs`; `Pipeline/{RagPipeline,PromptBuilder,EvidenceFormatter,LlmJson,RagModels}.cs`; `Controllers/RagController.cs`; `Errors.cs`
- `backend/tests/RagService.Tests/`
- `backend/.env.example`
- `frontend/squash-frontend/src/app/soc/` — `soc-models.ts`, `soc-api.ts`, `graph-api.ts`, `rag-api.ts`, `soc-page/`, `alerts-panel/`, `incidents-panel/`, `ask-panel/`, `evidence-panel/`, `graph-view/`, `evidence-mapping.ts`

**Modified**
- SocService: `Detection/DetectionOptions.cs`, `Detection/Rules/UnauthorisedEndpointAccessRule.cs`, `Models/SocConstants.cs`, `Program.cs`, `appsettings.json`
- GraphService: `Mapping/RuleThreatMap.cs`, `Program.cs`, `appsettings.json`
- `graph-data/app-structure.json`, `graph-data/knowledge/{curate.py,CURATION.md,security-knowledge.json}`, `graph-data/README.md`
- `backend/src/ApiGateway/ocelot.json`, `backend/docker-compose.yml`, `backend/SquashPlatform.slnx`, `.gitignore`
- `postman/PotchMatch-Demo.postman_collection.json` (+ environment)
- Angular: `app.ts`, `app.html`, `package.json`

---

### Task 1: Rule 5 (GRAPH_ACCESS) and Rule 2 exclusion

**Files:**
- Create: `backend/src/SocService/Detection/SensitivePaths.cs`, `backend/src/SocService/Detection/Rules/GraphAccessRule.cs`
- Modify: `DetectionOptions.cs` (add `GraphAccessOptions GraphAccess`), `SocConstants.cs` (`Rules.GraphAccess = "GRAPH_ACCESS"`), `UnauthorisedEndpointAccessRule.cs`, `Program.cs` (register rule), `appsettings.json` (`DetectionRules:GraphAccess`)
- Test: `backend/tests/SocService.Tests/GraphAccessRuleTests.cs`, extend the Rule 2 tests file, rules-endpoint test

**Interfaces:**
- Consumes: `IDetectionRule`, `RuleMatch(DedupKey, Description, AffectedService, AffectedUser, AffectedEntity, EventIds)`, `SocEvent`.
- Produces: `static bool SensitivePaths.IsSensitive(string? endpoint, IReadOnlyList<string> prefixes)` — case-insensitive; matches when endpoint starts with a prefix, or equals the prefix without its trailing `/`. `DetectionOptions.GraphAccessOptions { int Threshold = 3; int WindowMinutes = 5; List<string> SensitivePathPrefixes = ["/api/graph/", "/api/rag/"] }`.

- [ ] **Step 1: Write failing tests**
  - `SensitivePaths_CaseAndBarePrefix`: `"/API/Graph/incidents"` → true; `"/api/rag"` → true; `"/api/rag/ask"` → true; `"/api/graphql"` → false; `"/api/soc/alerts"` → false; null → false.
  - `GraphAccess_ThreeDenialsSameUser_Fires`: 3 `UNAUTHORISED_ACCESS` events, userId `u1`, endpoints `/api/graph/incidents` ×2 + `/api/rag/ask`, within 5 min → one match: DedupKey `GRAPH_ACCESS:user:u1`, AffectedUser `u1`, AffectedService `graph-service`, AffectedEntity = endpoint of the latest event, 3 EventIds.
  - `GraphAccess_TwoDenials_NoMatch`; `GraphAccess_OutsideWindow_NoMatch` (one event 6 min old).
  - `GraphAccess_NoUser_GroupsByIp`: 3 events, no userId, same sourceIp → DedupKey `GRAPH_ACCESS:ip:{ip}`, AffectedUser null.
  - `GraphAccess_AuthTokenRejected_Counts` (2 UNAUTHORISED_ACCESS + 1 AUTH_TOKEN_REJECTED on sensitive paths → fires).
  - `GraphAccess_RagMostTargeted_AffectsRagService` (2× `/api/rag/ask`, 1× `/api/graph/x` → `rag-service`); tie → `graph-service`.
  - `UnauthorisedAccess_IgnoresSensitivePaths`: 3 denials on `/api/graph/incidents` → Rule 2 no match; 3 on `/api/soc/alerts` → Rule 2 still matches.
  - `Rules_ListsGraphAccess`: rules endpoint contains `GRAPH_ACCESS` with non-empty Purpose, InputEventTypes, DetectionCondition, Severity `HIGH`, AlertGenerated, RecommendedResponse.
- [ ] **Step 2: Run** `dotnet test backend/tests/SocService.Tests` — Expected: FAIL (types missing).
- [ ] **Step 3: Implement** `GraphAccessRule(IOptions<DetectionOptions>)`: `RuleName = SocConstants.Rules.GraphAccess`, `Severity = High`, `InputEventTypes = ["AUTH_TOKEN_REJECTED", "UNAUTHORISED_ACCESS"]`, documentation strings from spec §6.1 (Purpose, DetectionCondition built from config values, AlertGenerated, RecommendedResponse "Review the account's role and recent activity; revoke its sessions and block the source IP if the attempts were not expected."). Grouping/ordering mirrors Rule 2. Description: `"{n} denied requests to sensitive graph/RAG endpoints by {user|IP x} within {w} minutes."`. Rule 2 filters out `SensitivePaths.IsSensitive(e.Endpoint, options.Value.GraphAccess.SensitivePathPrefixes)`.
- [ ] **Step 4: Run** — Expected: all SocService tests PASS.
- [ ] **Step 5: Commit** — `feat(soc): Rule 5 GRAPH_ACCESS for denied graph/RAG access`

---

### Task 2: Cypher guard

**Files:**
- Create: `backend/src/GraphService/Rag/CypherGuard.cs`
- Test: `backend/tests/GraphService.Tests/CypherGuardTests.cs`

**Interfaces:**
- Produces: `record CypherGuardResult(bool Allowed, string? Reason, string Cypher)`; `static CypherGuardResult CypherGuard.Check(string? cypher, int defaultLimit = 200)` — `Cypher` is the statement to execute (trailing `;` removed, ` LIMIT {defaultLimit}` appended when no `LIMIT` keyword outside strings/comments).

- [ ] **Step 1: Write failing tests** (theory data where natural)
  - Rejected, with Reason naming the keyword: `CREATE (n)`, `MATCH (n) SET n.x=1 RETURN n`, `MERGE`, `MATCH (n) DETACH DELETE n`, `REMOVE`, `DROP INDEX x`, `LOAD CSV FROM 'x' AS r RETURN r`, `FOREACH`, `CALL { MATCH (n) RETURN n } IN TRANSACTIONS RETURN 1`, `USE neo4j MATCH (n) RETURN n`, `CALL dbms.components()`, `CALL db.createLabel('x')`, `RETURN apoc.create.node(['X'],{})`, lowercase `match (n) delete n`.
  - Allowed: `CALL db.labels() YIELD label RETURN label`, `RETURN apoc.text.join(['a'], ',')`, `RETURN apoc.coll.toSet([1,1])`.
  - `Guard_KeywordsInsideIdentifiersOrStrings_Allowed`: `MATCH (a:Alert) WHERE a.createdAt IS NOT NULL RETURN a.offset`, `MATCH (d:Document {name:'Password Reset'}) RETURN d`, `MATCH (n) WHERE n.description CONTAINS 'set' RETURN n`, `MATCH (n) // delete everything\nRETURN n` → Allowed.
  - `Guard_MultiStatement_Rejected` (`MATCH (n) RETURN n; MATCH (m) RETURN m`); `Guard_TrailingSemicolon_Allowed`.
  - `Guard_NoReturn_Rejected`; `Guard_Empty_Rejected`.
  - `Guard_AppendsLimitOnlyWhenMissing`: no LIMIT → ends with `LIMIT 200`; `... RETURN n LIMIT 5` unchanged; `'limit'` only inside a string → appended.
- [ ] **Step 2: Run** `dotnet test backend/tests/GraphService.Tests --filter CypherGuard` — Expected: FAIL.
- [ ] **Step 3: Implement** — scan a copy with string literals (`'…'`, `"…"`, backtick identifiers, escapes) and comments replaced by spaces; regex word-boundary checks on the scrubbed text; procedure checks by `\b(dbms|db|apoc)\.[\w.]+` against the allow-lists in spec §4.2.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(graph): Cypher guard for LLM-generated queries`

---

### Task 3: GraphService internal query and schema endpoints

**Files:**
- Create: `backend/src/GraphService/Rag/{GuardedQueryRunner.cs, GraphSchemaDescriber.cs, QueryExamples.cs}`, `backend/src/GraphService/Controllers/InternalQueryController.cs`
- Modify: `Mapping/RuleThreatMap.cs` (+ `GRAPH_ACCESS`→`OWASP-A01-2025`), `Program.cs`, `appsettings.json` (`"GraphQuery": { "Key": "" }`), `backend/docker-compose.yml` (graph-service `GraphQuery__Key`)
- Test: `backend/tests/GraphService.Tests/{InternalQueryTests.cs, SchemaDescriberTests.cs}`, extend mapping tests

**Interfaces:**
- Consumes: `CypherGuard.Check` (Task 2), `GraphQueryRunner` conversion helpers (reuse its node/relationship/row shaping — refactor the shaping into a reusable internal method rather than duplicating it), `GraphQueries` constants, `GraphSchema`, Neo4jFixture.
- Produces (HTTP, consumed by Task 5):
  - `POST /api/internal/query` body `{ "cypher": string, "parameters": object|null }` → 200 `InternalQueryResponse(string Cypher, IReadOnlyDictionary<string,object?> Parameters, IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphRelDto> Relationships, IReadOnlyList<IReadOnlyDictionary<string,object?>> Rows, bool Truncated)`; 400 `{ error: "rejected", reason }` / `{ error: "cypher_error", reason }` (Neo4j `ClientException`, includes write-in-read-transaction refusals); 503 `{ error: "graph_unavailable" }` (`ServiceUnavailableException`/`SessionExpiredException`); 401 bad key.
  - `GET /api/internal/schema` → `GraphSchemaDescription(IReadOnlyList<LabelInfo> Labels, IReadOnlyList<RelTypeInfo> RelationshipTypes, SchemaSamples Samples, IReadOnlyList<QueryExample> Examples)`; `LabelInfo(string Label, string KeyProperty, IReadOnlyList<string> Properties, long Count)`; `RelTypeInfo(string Type, IReadOnlyList<string> From, IReadOnlyList<string> To, long Count)`; `SchemaSamples(ruleNames, threatIds, severities, alertStatuses, incidentStatuses, serviceNames)` (string lists, ≤ 30 each); `QueryExample(string Name, string Question, string Cypher)` — one per `GraphQueries` query, questions such as "Which alerts affect identity-service?" with `$name`/`$id` parameters noted.
  - `GuardedQueryRunner.RunAsync(string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken)` — read transaction with `TransactionConfig` timeout 10 s, stops after 200 records (`Truncated = true`).

- [ ] **Step 1: Write failing tests**
  - `InternalQuery_WrongOrMissingKey_401`, `InternalQuery_EmptyConfiguredKey_401`.
  - `InternalQuery_GuardRejects_400Rejected` (`CREATE (n) RETURN n`).
  - (collection neo4j) `InternalQuery_ReadOnly_ReturnsEvidence` — seeds loaded; `MATCH p=(c:Control)-[:MITIGATES]->(t:Threat {id:$id}) RETURN p` with `{id:"T1110"}` → nodes include M1032, relationships include MITIGATES, Truncated false.
  - (neo4j) `GuardedRunner_WriteRefusedByReadTransaction` — call `GuardedQueryRunner` directly (guard bypassed) with `CREATE (n:Hack) RETURN n` → throws `ClientException`; afterwards `MATCH (n:Hack)` count is 0.
  - (neo4j) `InternalQuery_SyntaxError_400CypherError`.
  - (neo4j) `GuardedRunner_TruncatesAt200` (`UNWIND range(1,500) AS i RETURN i` → 200 rows, Truncated true).
  - (neo4j) `Schema_DescribesLabelsTypesSamplesExamples` — Threat key `id`; MITIGATES from contains `Control`; samples.threatIds contains `T1110`; Examples count == 7 and each Cypher passes `CypherGuard.Check`.
  - `RuleThreatMap_GraphAccess_MapsToOwaspA01`.
- [ ] **Step 2: Run** `LD_PRELOAD=/tmp/claude-0/shim/v6shim.so dotnet test backend/tests/GraphService.Tests` (sandbox only) — Expected: FAIL.
- [ ] **Step 3: Implement**; controller `[Route("api/internal")] [AllowAnonymous]`, key check like the sync-key pattern (constant-time).
- [ ] **Step 4: Run** — Expected: PASS (whole GraphService suite).
- [ ] **Step 5: Commit** — `feat(graph): guarded read-only query and schema endpoints for RAG`

---

### Task 4: Seed additions and README reset note

**Files:**
- Modify: `graph-data/app-structure.json`, `graph-data/knowledge/curate.py` (+ regenerate `security-knowledge.json`, `CURATION.md`), `graph-data/README.md`
- Test: update `backend/tests/GraphService.Tests/SeedLoaderTests.cs` counts; add assertions

**Interfaces:**
- Produces graph data used by Tasks 6/7: Service `rag-service` (`kind: "application"`); Endpoints `POST /api/rag/ask`, `GET /api/rag/status` (`method`, `path`, `protected: true`, `discovered: false`) with `EXPOSES` from rag-service and `REQUIRES_ROLE` → Admin; `api-gateway DEPENDS_ON rag-service`; `rag-service DEPENDS_ON graph-service`; Threat stub `OWASP-A01-2025` (name only) with `TARGETS` → graph-service, rag-service. Curated edges: `M1018 MITIGATES OWASP-A01-2025`, `CTRL-SESSION-REVOCATION MITIGATES OWASP-A01-2025`, `DOC-SESSION-REVOCATION SUPPORTS_RESPONSE_TO OWASP-A01-2025`, each with a `why` citing OWASP A01:2025 "How to Prevent".

- [ ] **Step 1: Write failing tests**: `Load_RealSeedFiles_ExpectedCounts` updated to the new counts; new `Seeds_OwaspA01HasControlsAndTargets` (after loading both seeds: OWASP-A01-2025 has ≥2 incoming MITIGATES, TARGETS to rag-service and graph-service; rag-service EXPOSES 2 endpoints both REQUIRES_ROLE Admin).
- [ ] **Step 2: Run** (neo4j collection) — Expected: FAIL.
- [ ] **Step 3: Implement**: edit app-structure.json (preserve formatting/line endings); add the three edges to curate.py's curated tables; run `python3 -I graph-data/knowledge/curate.py`; replace the README "changing a seed" `down -v` advice with the volume-only reset from spec §4.4.
- [ ] **Step 4: Run** full GraphService suite — Expected: PASS; curate.py re-run produces no diff.
- [ ] **Step 5: Commit** — `feat(graph-data): rag-service structure and OWASP A01 controls`

---

### Task 5: RagService skeleton, clients, status endpoint, compose

**Files:**
- Create: `backend/src/RagService/{RagService.csproj, Dockerfile, .dockerignore, appsettings.json, Program.cs, Errors.cs}`, `Options/{GeminiOptions,GraphQueryOptions}.cs`, `Clients/{IGeminiClient,GeminiClient,IGraphQueryClient,GraphQueryClient,GraphDtos}.cs`, `Pipeline/LlmJson.cs`, `Controllers/RagController.cs` (status only in this task)
- Create: `backend/tests/RagService.Tests/{RagService.Tests.csproj, GeminiClientTests.cs, GraphQueryClientTests.cs, LlmJsonTests.cs, StatusTests.cs}`
- Create: `backend/.env.example`; Modify: `backend/SquashPlatform.slnx`, `backend/docker-compose.yml`, `.gitignore` (ensure `backend/.env`)

**Interfaces:**
- Consumes: Task 3 HTTP contracts.
- Produces:
  - `GeminiOptions { string? ApiKey; string Model = "gemini-2.5-flash"; string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/"; int TimeoutSeconds = 30; bool Enabled => !string.IsNullOrWhiteSpace(ApiKey) }` (section `Gemini`); `GraphQueryOptions { string Url; string Key }` (section `GraphQuery`).
  - `interface IGeminiClient { Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct); }` — POST `models/{Model}:generateContent`, body `{ systemInstruction:{parts:[{text}]}, contents:[{role:"user",parts:[{text}]}], generationConfig:{ temperature:0, responseMimeType:"application/json" } }`, header `x-goog-api-key`; returns `candidates[0].content.parts[0].text`; 429/5xx/timeout/missing candidate → `LlmUnavailableException`.
  - `interface IGraphQueryClient { Task<GraphSchemaDto> GetSchemaAsync(CancellationToken ct); Task<GraphQueryOutcome> QueryAsync(string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct); }`; `GraphQueryOutcome(bool Ok, GraphEvidence? Evidence, string? Error, string? Reason)`; 400 → Ok=false with error/reason; 503/connection failure → `GraphUnavailableException`. DTOs mirror Task 3 JSON (`GraphEvidence(Cypher, Parameters, Nodes, Relationships, Rows, Truncated)`, `EvidenceNode(Id, Label, Dictionary<string, JsonElement> Properties)`, `EvidenceRel(Id, Type, Start, End)`, schema DTOs incl. `Examples`).
  - `static T? LlmJson.Parse<T>(string text)` — tolerates ```` ```json ```` fences and leading/trailing prose (takes the outermost `{…}`); returns null if no valid JSON.
  - `GET /api/rag/status` → `RagStatus(bool Enabled, string? Model, string? Reason)`; disabled reason `"GEMINI_API_KEY is not set"`.
  - Program.cs: Serilog (`ServiceName = "rag-service"`, HTTP sink to SocService like GraphService), JWT bearer (copy GraphService), Consul registration `rag-service`, health checks, typed HttpClients.
  - Compose `rag-service` per spec §4.4 (`Gemini__ApiKey=${GEMINI_API_KEY:-}`, `Gemini__Model=${GEMINI_MODEL:-gemini-2.5-flash}`, `GraphQuery__Url=http://graph-service:8080`, `GraphQuery__Key`, Consul/SOC vars like graph-service; no host port). `backend/.env.example`: `GEMINI_API_KEY=` and `GEMINI_MODEL=gemini-2.5-flash` with a comment on where to get a key.

- [ ] **Step 1: Write failing tests** (fake `HttpMessageHandler`):
  - `Gemini_SendsKeyHeaderModelAndJsonMode` (asserts URL `.../models/gemini-2.5-flash:generateContent`, header `x-goog-api-key`, `responseMimeType` = `application/json`, temperature 0); `Gemini_ReturnsCandidateText`; `Gemini_429_ThrowsLlmUnavailable`; `Gemini_500_ThrowsLlmUnavailable`; `Gemini_NoCandidates_ThrowsLlmUnavailable`.
  - `GeminiJson_FencedOrPrefixed_Parsed`: ```` "```json\n{\"cypher\":\"MATCH (n) RETURN n\"}\n```" ```` and `"Here you go: {\"cypher\":\"…\"} thanks"` → parsed; `"no json"` → null.
  - `GraphClient_SendsKeyHeader`; `GraphClient_400_ReturnsRejectedOutcome`; `GraphClient_503_ThrowsGraphUnavailable`.
  - `Status_NoKey_Disabled` (Enabled false, Reason "GEMINI_API_KEY is not set"); `Status_WithKey_EnabledWithModel`.
- [ ] **Step 2: Run** `dotnet test backend/tests/RagService.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** — Expected: PASS; `dotnet build backend/SquashPlatform.slnx` clean.
- [ ] **Step 5: Commit** — `feat(rag): RagService skeleton with Gemini and graph clients`

---

### Task 6: RAG pipeline and ask endpoint

**Files:**
- Create: `backend/src/RagService/Pipeline/{RagPipeline.cs, PromptBuilder.cs, EvidenceFormatter.cs, RagModels.cs}`; Modify: `Controllers/RagController.cs`, `Program.cs`
- Test: `backend/tests/RagService.Tests/{RagPipelineTests.cs, EvidenceFormatterTests.cs, AskEndpointTests.cs}`

**Interfaces:**
- Consumes: Task 5 clients/DTOs/`LlmJson`.
- Produces:
  - `record AskRequest(string Question, string? AlertId, string? IncidentId)`.
  - `RagAnswer` / `RetrievalInfo(string Method, string Model, int Attempts, string? Explanation, int RowCount, bool Truncated, IReadOnlyList<string> UnverifiedCitations)` / `SourceRef(string Id, string? Name, string? Url)` — JSON exactly spec §5.4; `Method` = `"LLM-generated Cypher, validated read-only, executed on Neo4j"`; `Mode` = `"text2cypher"`.
  - `RagPipeline.AskAsync(AskRequest, CancellationToken) → Task<RagAnswer>` implementing spec §5.1 steps 1–6 (schema cached 10 min via `IMemoryCache`); context IDs merged into parameters **after** the model's parameters (model cannot override `alertId`/`incidentId`).
  - `EvidenceFormatter.DisplayName(EvidenceNode)` → first non-empty of `name`, `title`, `id`, `key`, `alertId`, `incidentId`, `eventId`, `userId`, else `Label`; `EvidenceFormatter.Triples(nodes, rels) → IReadOnlyList<string>` (`"{start} —{TYPE}→ {end}"`); `EvidenceFormatter.Citations(string answer) → IReadOnlyList<string>` (bracketed tokens).
  - `PromptBuilder`: `CypherSystem(GraphSchemaDto schema)`, `CypherUser(AskRequest)`, `RepairUser(AskRequest, string failedCypher, string reason)`, `AnswerSystem()`, `AnswerUser(string question, IReadOnlyList<rows ≤50>, IReadOnlyList<string> triples, bool truncated)` — rule text from spec §5.1 (read-only, parameters, return paths, follow Alert→Threat→Control and Document links, answer only from evidence, cite IDs in brackets, say what is not covered, 3–8 sentences). Expected model JSON: Cypher step `{cypher, parameters, explanation}`; answer step `{answer}`.
  - `POST /api/rag/ask` → 200 RagAnswer | 400 validation | 503 `rag_disabled` / `llm_unavailable` / `graph_unavailable` (exception → status mapping in the controller or a filter).
  - Logs (Serilog, via SOC sink): question length, attempts, row count, answered, latency — never the question text or key.

- [ ] **Step 1: Write failing tests** (fake `IGeminiClient` scripted per call, fake `IGraphQueryClient`):
  - `Ask_HappyPath_GeneratesExecutesAnswers` — answer text from 2nd Gemini call; `cypher`, `nodes`, `relationships`, `rows` from graph; `attempts` 1; `sources` = the Document nodes (`DOC-PASSWORD-RESET` with url null, `DOC-T1110` with url).
  - `Ask_ContextIdsMergedAndNotOverridable` — model returns parameters `{alertId:"evil"}`; request alertId `a1` → graph receives `alertId = "a1"`.
  - `Ask_Rejected_RepairsOnce_ThenSucceeds` (attempts 2; repair prompt contains the reason); `Ask_CypherError_RepairsOnce`; `Ask_TwoFailures_AnsweredFalseWithReason` (no exception; `answered` false; cypher = last failing one).
  - `Ask_NoRows_NoEvidenceAnswer_AnswerModelNotCalled` (Gemini called exactly once; answer "The knowledge graph has no evidence for this question.").
  - `Ask_EvidenceOver50Rows_TruncatedForPrompt` (answer prompt contains 50 rows; `retrieval.truncated` true).
  - `Answer_UnknownCitation_ReportedAsUnverified` (answer cites `[T1110]` (present) and `[T9999]` (absent) → `unverifiedCitations == ["T9999"]`).
  - `Ask_BadModelJson_LlmUnavailable503`; `Ask_LlmException_503`; `Ask_GraphUnavailable_503`; `Ask_NoKey_503RagDisabled`; `Ask_QuestionEmptyOrTooLong_400` (0 and 501 chars).
  - `EvidenceFormatter_TriplesAndDisplayNames`.
- [ ] **Step 2: Run** `dotnet test backend/tests/RagService.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(rag): text2cypher pipeline with grounded answers and evidence`

---

### Task 7: Gateway route, Postman RAG folder, backend end-to-end

**Files:**
- Modify: `backend/src/ApiGateway/ocelot.json`, `postman/PotchMatch-Demo.postman_collection.json`, `postman/PotchMatch-Demo.postman_environment.json`

**Interfaces:**
- Produces: route upstream `/api/rag/{everything}` → downstream same, `ServiceName: "rag-service"`, `UpstreamHttpMethod: ["Get", "Post"]`, same AuthenticationOptions, `RouteClaimsRequirement { "role": "Admin" }`, X-Forwarded-For like the graph route. Postman folder "Phase 2 — RAG": Login Admin (`adminToken`), RAG status, Ask "Why was this alert created and what should be done?" (body with `alertId: {{graphAlertId}}`), Ask "What controls can mitigate brute force attacks?", Ask "What is the likely impact if payment-service fails?", Rule 5 probe (Login player → 3× `GET /api/graph/incidents` with `{{authToken}}` expecting 403 → wait note → `GET /api/soc/alerts?status=OPEN` test asserts a `GRAPH_ACCESS` alert). Tests assert `answer` non-empty, `cypher` non-empty, `nodes.length > 0` when `answered`.

- [ ] **Step 1: Implement** route + Postman (validate JSON with a parser).
- [ ] **Step 2: End-to-end on a clean stack** (sandbox: workspace cloud-build helper incl. `rag-service`; compose with the IPv6 shim override; start a throw-away local Gemini stub — a tiny HTTP server returning canned `generateContent` JSON — and point `Gemini__BaseUrl` at it via a sandbox-only override file; nothing of the stub is committed):
  - No key: `GET /api/rag/status` → `enabled:false`; ask → 503 `rag_disabled`.
  - Stub key: failed-login scenario → ask with the alertId → 200 with evidence from the real graph (canned Cypher `MATCH p=(a:Alert {alertId:$alertId})-[:INDICATES]->(t:Threat)<-[:MITIGATES]-(c:Control) RETURN p`).
  - Player token ×3 on `/api/graph/incidents` → within ~45 s a GRAPH_ACCESS alert (HIGH, affectedService graph-service) and no new Unauthorised Endpoint Access alert for those requests; after sync, Alert `INDICATES` OWASP-A01-2025.
  - Player also ×3 on `/api/soc/alerts` → escalated incident with 2 rules.
  - Player token on `/api/rag/ask` → 403; no token → 401.
  - Capture outputs in the report; leave the stack down.
- [ ] **Step 3: Run** `dotnet test backend/SquashPlatform.slnx` (sandbox: with the shim) — Expected: all pass.
- [ ] **Step 4: Commit** — `feat(gateway): admin-only /api/rag route; Postman RAG folder`

---

### Task 8: Angular SOC view — APIs, alerts and incidents

**Files:**
- Create: `src/app/soc/{soc-models.ts, soc-api.ts, graph-api.ts, rag-api.ts}`, `src/app/soc/soc-page/*`, `src/app/soc/alerts-panel/*`, `src/app/soc/incidents-panel/*` (+ `.spec.ts` files)
- Modify: `src/app/app.ts`, `src/app/app.html` (and `app.css` if needed)

**Interfaces:**
- Consumes: `API_BASE_URL` (`core/api-config.ts`), existing auth interceptor and `Auth.currentPlayer()` (role `'Admin'`), backend endpoints `/api/soc/alerts?status=&severity=&ruleName=`, `PATCH /api/soc/alerts/{id}/status` body `{ status }`, `/api/graph/incidents`, `/api/rag/status`, `/api/rag/ask`.
- Produces:
  - TS models mirroring backend JSON: `SocAlert`, `GraphIncident` (from `rows` of the incidents query), `RagStatus`, `AskRequest`, `RagAnswer`, `EvidenceNode`, `EvidenceRel`, `SourceRef`, `RetrievalInfo`.
  - Services (`providedIn: 'root'`): `SocApi.getAlerts(filter)`, `SocApi.setStatus(id, status)`; `GraphApi.getIncidents(status?)`; `RagApi.status()`, `RagApi.ask(req)`.
  - `SocPage` holds signals `question`, `context: { alertId?: string; incidentId?: string }`; panels emit `askAbout({question, alertId?|incidentId?})` which `SocPage` passes to the Ask panel (Task 9 consumes `question`/`context` inputs).
  - App shell: `view` type becomes `'map' | 'dashboard' | 'soc'`; SOC button rendered only when role is Admin; after an Admin logs in the view becomes `'soc'`.
  - Error handling: HTTP 401 in any panel → message "Session expired — please log in again"; other errors → message with status.

- [ ] **Step 1: Write failing tests** (vitest via `npx ng test --watch=false`, `HttpTestingController`):
  - `SocButton_OnlyForAdmin` (Player: no SOC button; Admin: button present and SOC view shown after login).
  - `AlertsPanel_RendersRowsAndFilters` (status filter adds `status=OPEN`); `AlertsPanel_AcknowledgeCallsPatch`; `AlertsPanel_WhyEmitsAskAbout` (question "Why was this alert created and what should be done?", alertId).
  - `IncidentsPanel_AskEmitsIncidentQuestion` ("What does the graph know about this incident, its affected services and possible response actions?", incidentId); shows escalated badge when `escalated`.
  - `AlertsPanel_401_ShowsSessionExpired`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** (standalone components, signals, existing styling conventions; 30 s auto-refresh with cleanup on destroy).
- [ ] **Step 4: Run** tests + `npx ng build` — Expected: PASS / build succeeds.
- [ ] **Step 5: Commit** — `feat(frontend): admin SOC view with alerts and incidents`

---

### Task 9: Angular Ask panel, evidence panel and graph view

**Files:**
- Create: `src/app/soc/{evidence-mapping.ts, ask-panel/*, evidence-panel/*, graph-view/*}` (+ specs); Modify: `soc-page` to include them; `package.json` (+ `cytoscape`, exact pinned version)

**Interfaces:**
- Consumes: Task 8 models/services and `SocPage` `question`/`context` signals.
- Produces:
  - `evidence-mapping.ts`: `displayName(node: EvidenceNode): string` (same precedence as backend `EvidenceFormatter.DisplayName`); `toTriples(nodes, rels): string[]` (`"{start} —{TYPE}→ {end}"`); `toCytoscapeElements(nodes, rels): ElementDefinition[]` (node data `{ id, label: displayName, type: node.label }`, edge data `{ id, source, target, label: type }`).
  - `AskPanel`: textarea (maxlength 500), example chips (spec §5.1 questions), Ask button; on init calls `RagApi.status()` — when disabled shows the reason + "Set GEMINI_API_KEY in backend/.env and restart rag-service" and disables input; shows 503 messages verbatim; emits/holds the latest `RagAnswer`.
  - `EvidencePanel` (input `answer: RagAnswer`): answer text (text binding only), retrieval line (method, model, attempts, rows, truncated) + explanation, unverified citations warning when non-empty, `GraphView`, relationships list from `toTriples`, Cypher + parameters (`<pre>`), sources (link when `url`, else id).
  - `GraphView` (inputs nodes, rels): Cytoscape instance in a fixed-height container, `cose` layout, node colour by `type` (fixed map for the 12 labels + default), destroyed on component destroy.

- [ ] **Step 1: Write failing tests**: `Mapping_TriplesAndDisplayNames`; `Mapping_CytoscapeElements` (ids, source/target, labels); `AskPanel_DisabledWhenRagDisabled` (reason text shown, textarea disabled); `AskPanel_PostsQuestionWithContext` (body contains alertId when set); `EvidencePanel_ShowsCypherSourcesAndRetrieval`; `EvidencePanel_UnverifiedCitationsWarning`. (GraphView: test that it builds elements from inputs; Cytoscape may be mocked.)
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement**; `npm install cytoscape@<current> --save-exact` (add `@types/cytoscape` only if the package has no bundled types).
- [ ] **Step 4: Run** tests + `npx ng build` (and the frontend Docker image builds via the workspace helper) — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(frontend): ask-the-graph panel with evidence graph`
