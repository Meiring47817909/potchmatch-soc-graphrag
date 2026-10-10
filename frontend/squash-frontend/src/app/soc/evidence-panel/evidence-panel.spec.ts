import { TestBed } from '@angular/core/testing';
import { EvidencePanel } from './evidence-panel';
import { RagAnswer } from '../soc-models';

vi.mock('cytoscape', () => ({ default: vi.fn(() => ({ destroy: vi.fn(), layout: vi.fn(() => ({ run: vi.fn() })) })) }));

const base = (over: Partial<RagAnswer> = {}): RagAnswer => ({
  question: 'Why?',
  answered: true,
  answer: 'Because of [ALT-1] and <b>x</b>',
  mode: 'text2cypher',
  retrieval: {
    method: 'LLM-generated Cypher',
    model: 'gemini-2.5-flash',
    attempts: 2,
    explanation: 'Follows the alert to the threat.',
    rowCount: 6,
    truncated: true,
    unverifiedCitations: [],
  },
  cypher: 'MATCH p = (a:Alert {alertId: $alertId}) RETURN p',
  parameters: { alertId: 'ALT-1' },
  nodes: [
    { id: '1', label: 'Alert', properties: { alertId: 'ALT-1' } },
    { id: '2', label: 'Threat', properties: { name: 'Brute force' } },
    { id: '3', label: 'Document', properties: { id: 'DOC-1', name: 'Reset guide', url: 'https://x.test/doc' } },
    { id: '4', label: 'Document', properties: { id: 'DOC-2' } },
  ],
  relationships: [{ id: 'r1', type: 'INDICATES', start: '1', end: '2' }],
  rows: [],
  sources: [
    { id: 'DOC-1', name: 'Reset guide', url: 'https://x.test/doc' },
    { id: 'DOC-2', name: null, url: null },
  ],
  ...over,
});

function render(a: RagAnswer) {
  TestBed.configureTestingModule({ imports: [EvidencePanel] });
  const fixture = TestBed.createComponent(EvidencePanel);
  fixture.componentRef.setInput('answer', a);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('EvidencePanel', () => {
  it('EvidencePanel_ShowsCypherSourcesAndRetrieval', () => {
    const el = render(base());
    const text = el.textContent ?? '';
    expect(text).toContain('Because of [ALT-1] and <b>x</b>'); // text binding, not HTML
    expect(el.querySelector('.answer b')).toBeNull();
    expect(text).toContain('LLM-generated Cypher');
    expect(text).toContain('gemini-2.5-flash');
    expect(text).toContain('Attempts: 2');
    expect(text).toContain('Rows: 6');
    expect(text).toContain('truncated');
    expect(text).toContain('Follows the alert to the threat.');
    expect(el.querySelector('pre.cypher')?.textContent).toContain('MATCH p = (a:Alert');
    expect(el.querySelector('pre.params')?.textContent).toContain('"alertId": "ALT-1"');
    expect(text).toContain('ALT-1 —INDICATES→ Brute force');
    const link = el.querySelector('.sources a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('https://x.test/doc');
    expect(link.textContent).toContain('Reset guide');
    expect(el.querySelectorAll('.sources li').length).toBe(2);
    expect(el.querySelectorAll('.sources li')[1].textContent).toContain('DOC-2');
    expect(el.querySelector('app-graph-view')).not.toBeNull();
    expect(el.querySelector('.warning')).toBeNull();
  });

  it('EvidencePanel_UnverifiedCitationsWarning', () => {
    const a = base();
    a.retrieval.unverifiedCitations = ['T9999', 'M0'];
    const el = render(a);
    const w = el.querySelector('.warning');
    expect(w?.textContent).toContain('T9999');
    expect(w?.textContent).toContain('M0');
  });

  it('EvidencePanel_UnansweredShowsRowsWhenNoRelationships', () => {
    const el = render(
      base({
        answered: false,
        answer: 'I could not build a valid graph query for this question.',
        nodes: [],
        relationships: [],
        rows: [{ error: 'cypher_error', reason: 'Syntax error near X' }],
      }),
    );
    expect(el.textContent).toContain('Syntax error near X');
    expect(el.querySelector('app-graph-view')).toBeNull();
  });

  it('EvidencePanel_HidesModelWhenEmpty', () => {
    const a = base();
    a.retrieval.model = '';
    expect(render(a).textContent).not.toContain('Model:');
  });
});
