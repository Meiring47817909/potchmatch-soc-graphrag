# Security knowledge graph: Graph Builder pipeline

Builds the Threat / Control / Vulnerability / Document knowledge from `sources/*.md` using Neo4j LLM Graph Builder, then exports it as JSON for the PotchMatch graph seed.

1. Open https://llm-graph-builder.neo4jlabs.com/ and connect to your Aura instance (Connect to Neo4j: paste the Aura URI, username `neo4j`, and password; use the same database that will be queried).
2. Open Graph Enhancement > Graph Schema and set it to exactly these 4 node labels: `Threat`, `Control`, `Vulnerability`, `Document`; and exactly these 4 relationship types: `MITIGATES`, `EXPLOITS`, `DESCRIBES`, `SUPPORTS_RESPONSE_TO`. Use "Use these schema only" / no additional entity types.
3. Upload all files in `graph-data/knowledge/sources/` (12 `.md` files).
4. Select all uploaded files and click Generate Graph. Wait until every file shows Completed.
5. Open Aura Query (console.neo4j.io > Query) on the same instance and run the contents of `export.cypher`. It returns one row with `nodes` and `relationships`.
6. Download the result as JSON (Export / Download result > JSON).
7. Save it as `graph-data/knowledge/security-knowledge.raw.json`.
8. Tell Claude the file is saved so it can normalise IDs (T1110, M1032, VULN-WEAK-PASSWORDS, DOC-PASSWORD-RESET, ...) and build the seed.

Notes
- The export query uses APOC (`apoc.map.removeKeys`), which Aura provides. If it errors, tell Claude.
- Graph Builder stores names in the `id` property; the export returns `name` as `coalesce(name, id)`.
- Sources are paraphrased from MITRE ATT&CK (c) The MITRE Corporation (ATT&CK Terms of Use) and OWASP Top 10 (CC BY-SA 4.0); the four procedures are PotchMatch internal documents.
