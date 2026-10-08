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

GraphService loads both at startup, in that order.

## Inspecting the graph in Neo4j Desktop / Browser

- Bolt (remote connection): `bolt://localhost:7687`, user `neo4j`, password `potchmatch-dev`
- Neo4j Browser: `http://localhost:7474`
