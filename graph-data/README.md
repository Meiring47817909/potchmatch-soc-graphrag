# graph-data

Seed files for the GraphService Neo4j knowledge graph. They are mounted read-only into graph-service at `/app/graph-data` (config `GraphSeed:Directory`).

## Seed format

Both seeds share one format (camelCase JSON):

```json
{
  "nodes": [ { "label": "Service", "key": "api-gateway", "properties": { "kind": "gateway" } } ],
  "relationships": [ { "type": "DEPENDS_ON", "from": { "label": "Service", "key": "api-gateway" }, "to": { "label": "Service", "key": "identity-service" } } ]
}
```

- `label` must be a label from the graph schema and `key` is the value of that label's key property (Service `name`, Endpoint `key`, Threat `id`, ...).
- `type` must be a known relationship type. Both endpoints must be declared in the same file or in a seed loaded earlier.
- The loader validates the whole file first and writes nothing if any error is found. Writes are MERGEs, so loading is idempotent.

## Files and load order

1. `app-structure.json` - services, endpoints, assets, roles, dependencies, and three Threat stubs (`T1110`, `T1078`, `T1499`, name only) so `TARGETS` can be expressed.
2. `knowledge/security-knowledge.json` - curated threats, controls, vulnerabilities and documents; MERGEs onto the Threat stubs. See `knowledge/README.md` for how it is produced. The committed file is currently the fixture (`origin: "fixture"`).

GraphService loads both at startup, in that order. If either file is invalid, graph-service logs every validation error and exits with code 1.

## Changing a seed file: reset Neo4j

Seeding only ever MERGEs. Editing a seed file adds and updates nodes and relationships on the next start, but anything you **remove or rename** stays in the existing `neo4j-data` volume. After changing a seed file, reset Neo4j before restarting:

- Full reset (also drops synced players, alerts, events and incidents; they are re-synced from the other services):

  ```bash
  cd backend
  docker compose down -v
  docker compose up -d
  ```

- Or reset only the seeded knowledge, then restart graph-service. In Neo4j Browser / Desktop run:

  ```cypher
  MATCH (n) WHERE n:Threat OR n:Control OR n:Vulnerability OR n:Document DETACH DELETE n
  ```

  then `docker compose restart graph-service`. (Services, endpoints, assets and roles from `app-structure.json` are not covered by this query; use the full reset if you removed any of those.)

## Inspecting the graph in Neo4j Desktop / Browser

With the compose stack running (Neo4j publishes `7474` and `7687`):

**Neo4j Desktop**

1. Click **Add** (top right) and choose **Remote connection**.
2. Name it (e.g. `PotchMatch`) and enter the URL `bolt://localhost:7687`.
3. Authentication: username `neo4j`, password `potchmatch-dev`.
4. Click **Connect**.
5. Open the connection with **Query** (Cypher editor with graph results) or **Explore** (visual exploration).

**Neo4j Browser** (no install): open `http://localhost:7474`, connect URL `bolt://localhost:7687`, user `neo4j`, password `potchmatch-dev`.

**Example query: from a user to the alerts they caused, the threat indicated, the controls that mitigate it, and the supporting documents**

```cypher
MATCH chain = (u:User)-[:TRIGGERED]->(e:Event)-[:CREATED_ALERT]->(a:Alert)-[:INDICATES]->(t:Threat)<-[:MITIGATES]-(c:Control)
OPTIONAL MATCH controlDoc = (d:Document)-[:DESCRIBES]->(c)
OPTIONAL MATCH responseDoc = (r:Document)-[:SUPPORTS_RESPONSE_TO]->(t)
RETURN chain, controlDoc, responseDoc
LIMIT 50
```

Run it after triggering the demo (e.g. several failed logins) so the live User/Event/Alert part of the chain exists.
