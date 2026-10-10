import { EvidenceNode, EvidenceRel } from './soc-models';
import { displayName, toCytoscapeElements, toTriples } from './evidence-mapping';

const node = (id: string, label: string, properties: Record<string, unknown> = {}): EvidenceNode => ({
  id,
  label,
  properties,
});

describe('evidence-mapping', () => {
  it('Mapping_TriplesAndDisplayNames', () => {
    const nodes = [
      node('4:a:1', 'Alert', { alertId: 'ALT-1', id: 'x' }),
      node('4:a:2', 'Threat', { name: '  Brute force\n attack ', id: 'OWASP-A07' }),
      node('4:a:3', 'Service', { name: '', title: '', id: 42 }),
      node('4:a:4', 'Control', {}),
    ];
    const rels: EvidenceRel[] = [
      { id: 'r1', type: 'INDICATES', start: '4:a:1', end: '4:a:2' },
      { id: 'r2', type: 'MITIGATES', start: '4:a:3', end: 'unknown\t\nid' },
    ];
    expect(displayName(nodes[0])).toBe('x'); // id precedes alertId
    expect(displayName(nodes[1])).toBe('Brute force attack');
    expect(displayName(nodes[2])).toBe('42');
    expect(displayName(nodes[3])).toBe('Control');
    expect(toTriples(nodes, rels)).toEqual([
      'x —INDICATES→ Brute force attack',
      '42 —MITIGATES→ unknown id',
    ]);
  });

  it('Mapping_CytoscapeElements', () => {
    const nodes = [node('1', 'Alert', { alertId: 'ALT-1' }), node('2', 'Threat', { name: 'T' })];
    const rels: EvidenceRel[] = [{ id: 'r1', type: 'INDICATES', start: '1', end: '2' }];
    expect(toCytoscapeElements(nodes, rels)).toEqual([
      { data: { id: '1', label: 'ALT-1', type: 'Alert' } },
      { data: { id: '2', label: 'T', type: 'Threat' } },
      { data: { id: 'r1', source: '1', target: '2', label: 'INDICATES' } },
    ]);
  });

  it('Mapping_CytoscapeElements_DropsEdgesWithMissingEndpoints', () => {
    const nodes = [node('1', 'Alert')];
    const rels: EvidenceRel[] = [{ id: 'r1', type: 'X', start: '1', end: '9' }];
    expect(toCytoscapeElements(nodes, rels).length).toBe(1);
  });
});
