// Run in Aura Query (Neo4j 5). Returns ONE row: {nodes, relationships}.
// Graph Builder keeps the entity name in `id`; description (if any) in `description`.
// Excludes Chunk nodes, uploaded-file Document nodes (they have fileName) and embeddings.
MATCH (n)
WHERE any(l IN labels(n) WHERE l IN ['Threat','Control','Vulnerability','Document'])
  AND n.fileName IS NULL
  AND NOT n:Chunk
WITH collect(n) AS ns
WITH ns,
  [n IN ns | {
    label: [l IN labels(n) WHERE l IN ['Threat','Control','Vulnerability','Document']][0],
    name: coalesce(n.name, n.id),
    id: n.id,
    description: n.description,
    properties: apoc.map.removeKeys(properties(n),
      [k IN keys(n) WHERE toLower(k) CONTAINS 'embedding'])
  }] AS nodes
OPTIONAL MATCH (a)-[r]->(b)
WHERE a IN ns AND b IN ns
  AND type(r) IN ['MITIGATES','EXPLOITS','DESCRIBES','SUPPORTS_RESPONSE_TO']
WITH nodes, collect(CASE WHEN r IS NULL THEN NULL ELSE {
    type: type(r),
    fromLabel: [l IN labels(a) WHERE l IN ['Threat','Control','Vulnerability','Document']][0],
    fromName: coalesce(a.name, a.id),
    toLabel: [l IN labels(b) WHERE l IN ['Threat','Control','Vulnerability','Document']][0],
    toName: coalesce(b.name, b.id)
  } END) AS relationships
RETURN nodes, relationships;
