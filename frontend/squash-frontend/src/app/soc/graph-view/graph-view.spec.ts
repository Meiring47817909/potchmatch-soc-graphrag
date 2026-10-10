import { TestBed } from '@angular/core/testing';
import { GraphView } from './graph-view';
import { EvidenceNode, EvidenceRel } from '../soc-models';

const instances: { opts: any; destroy: ReturnType<typeof vi.fn>; layout: ReturnType<typeof vi.fn> }[] = [];
vi.mock('cytoscape', () => ({
  default: vi.fn((opts: any) => {
    const inst = { opts, destroy: vi.fn(), layout: vi.fn(() => ({ run: vi.fn() })) };
    instances.push(inst);
    return inst;
  }),
}));

const nodes: EvidenceNode[] = [
  { id: '1', label: 'Alert', properties: { alertId: 'ALT-1' } },
  { id: '2', label: 'Threat', properties: { name: 'Brute force' } },
];
const rels: EvidenceRel[] = [{ id: 'r1', type: 'INDICATES', start: '1', end: '2' }];

describe('GraphView', () => {
  beforeEach(() => (instances.length = 0));

  it('GraphView_BuildsElementsFromInputsAndDestroys', async () => {
    TestBed.configureTestingModule({ imports: [GraphView] });
    const fixture = TestBed.createComponent(GraphView);
    fixture.componentRef.setInput('nodes', nodes);
    fixture.componentRef.setInput('rels', rels);
    fixture.detectChanges();
    await vi.waitFor(() => expect(instances.length).toBe(1));
    const opts = instances[0].opts;
    expect(opts.container).toBe(fixture.nativeElement.querySelector('.canvas'));
    expect(opts.elements.map((e: any) => e.data.id)).toEqual(['1', '2', 'r1']);
    expect(opts.layout.name).toBe('cose');
    fixture.destroy();
    expect(instances[0].destroy).toHaveBeenCalled();
  });

  it('GraphView_RedrawsOnInputChange', async () => {
    TestBed.configureTestingModule({ imports: [GraphView] });
    const fixture = TestBed.createComponent(GraphView);
    fixture.componentRef.setInput('nodes', nodes);
    fixture.componentRef.setInput('rels', rels);
    fixture.detectChanges();
    await vi.waitFor(() => expect(instances.length).toBe(1));
    fixture.componentRef.setInput('nodes', [...nodes, { id: '3', label: 'Control', properties: {} }]);
    fixture.detectChanges();
    await vi.waitFor(() => expect(instances.length).toBe(2));
    expect(instances[0].destroy).toHaveBeenCalled();
    expect(instances[1].opts.elements.length).toBe(4);
  });
});
