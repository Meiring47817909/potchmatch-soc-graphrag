import type { ElementDefinition } from 'cytoscape';
import { EvidenceNode, EvidenceRel } from './soc-models';

// Same precedence as backend EvidenceFormatter.DisplayName.
const DISPLAY_KEYS = ['name', 'title', 'id', 'key', 'alertId', 'incidentId', 'eventId', 'userId'];

// Mirrors EvidenceFormatter.OneLine: control/format/separator characters become one space.
function oneLine(s: string): string {
  return s
    .replace(/[\p{Cc}\p{Cf}\p{Zl}\p{Zp}\s]+/gu, ' ')
    .trim();
}

function firstString(node: EvidenceNode): string | null {
  for (const key of DISPLAY_KEYS) {
    const v = node.properties?.[key];
    const s = typeof v === 'string' ? v : typeof v === 'number' ? String(v) : null;
    if (s !== null && s.trim() !== '') return s.trim();
  }
  return null;
}

export function displayName(node: EvidenceNode): string {
  return oneLine(firstString(node) ?? node.label);
}

export function toTriples(nodes: EvidenceNode[], rels: EvidenceRel[]): string[] {
  const byId = new Map<string, EvidenceNode>();
  for (const n of nodes) if (!byId.has(n.id)) byId.set(n.id, n);
  const name = (id: string) => {
    const n = byId.get(id);
    return n ? displayName(n) : oneLine(id);
  };
  return rels.map((r) => `${name(r.start)} —${r.type}→ ${name(r.end)}`);
}

export function toCytoscapeElements(nodes: EvidenceNode[], rels: EvidenceRel[]): ElementDefinition[] {
  const ids = new Set(nodes.map((n) => n.id));
  return [
    ...nodes.map((n) => ({ data: { id: n.id, label: displayName(n), type: n.label } })),
    // Cytoscape throws on an edge whose endpoints are not in the graph.
    ...rels
      .filter((r) => ids.has(r.start) && ids.has(r.end))
      .map((r) => ({ data: { id: r.id, source: r.start, target: r.end, label: r.type } })),
  ];
}
