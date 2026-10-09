# Security knowledge graph: Graph Builder pipeline

Builds the Threat / Control / Vulnerability / Document knowledge with Neo4j LLM Graph Builder from two inputs, then exports it as JSON for the PotchMatch graph seed:
- the official MITRE ATT&CK and OWASP pages listed in `sources/web-sources.txt` (11 URLs), and
- the four PotchMatch response procedures in `sources/procedure-*.md` (our own documents).

`reference-summaries/` holds AI-assisted paraphrases of the same MITRE/OWASP pages; they are NOT uploaded (see its README).

0. If you do not have an Aura instance yet: sign in at https://console.neo4j.io, create a free AuraDB instance, and save the generated credentials (connection URI, username `neo4j`, and the password; the password is shown only once, so download the credentials file).
1. Open https://llm-graph-builder.neo4jlabs.com/ and connect to your Aura instance (Connect to Neo4j: paste the Aura URI, username `neo4j`, and password; use the same database that will be queried).
2. Open Graph Settings > Entity Extraction Settings and add exactly these 4 patterns (Source / Type / Target; type a new name and pick "Create"), then click Apply:
   - `Control` -[`MITIGATES`]-> `Threat`
   - `Threat` -[`EXPLOITS`]-> `Vulnerability`
   - `Document` -[`DESCRIBES`]-> `Control`
   - `Document` -[`SUPPORTS_RESPONSE_TO`]-> `Threat`
3. Add the sources:
   - Drag & drop the 4 `sources/procedure-*.md` files.
   - For each of the 11 URLs in `sources/web-sources.txt`, use the Web / website-link source button (below Drag & Drop) and submit the URL.
   - Check the file list shows 15 sources (4 files + 11 web pages).
4. Select all uploaded files and click Generate Graph. Wait until every file shows Completed.
5. Open Aura Query (console.neo4j.io > Query) on the same instance and run the contents of `export.cypher`. It returns one row with `nodes` and `relationships`.
6. Download the result as JSON (Export / Download result > JSON).
7. Save it as `graph-data/knowledge/security-knowledge.raw.json`.
8. Tell Claude the file is saved so it can normalise IDs (T1110, M1032, VULN-WEAK-PASSWORDS, DOC-PASSWORD-RESET, ...) and build the seed.
9. Curate: `python3 graph-data/knowledge/curate.py` (Python 3, stdlib only). It reads the raw export and writes `security-knowledge.json` (the seed) and `CURATION.md` (every merge, dropped category and curated addition, with final counts). The alias/merge table, the kept set and the official MITRE mitigation tables are explicit data at the top of `curate.py`; change them there and re-run, never edit the two outputs by hand.

After `security-knowledge.json` changes, reset the local Neo4j so removed or renamed knowledge does not linger (seeding only MERGEs): `docker compose down -v`, or run `MATCH (n) WHERE n:Threat OR n:Control OR n:Vulnerability OR n:Document DETACH DELETE n` and restart graph-service. See `../README.md`.

Notes
- The export query uses APOC (`apoc.map.removeKeys`), which Aura provides. If it errors, tell Claude.
- Graph Builder stores names in the `id` property; the export returns `name` as `coalesce(name, id)`.
- Knowledge is extracted from the official MITRE ATT&CK pages (c) The MITRE Corporation (ATT&CK Terms of Use) and OWASP Top 10:2025 (CC BY-SA 4.0); the four procedures are PotchMatch internal documents.
- The real pages mention many more techniques and mitigations than PotchMatch needs (e.g. M1018 User Account Management on T1110). Curation (Task 11) keeps the required IDs, may keep relevant extras, and records every change in `CURATION.md`. MITRE pages rarely name vulnerabilities, so `VULN-*` nodes may need to be curated in, sourced from OWASP A01 and the procedures.
