import { Component, DestroyRef, ElementRef, effect, inject, input, viewChild } from '@angular/core';
import type { Core, StylesheetJson } from 'cytoscape';
import { EvidenceNode, EvidenceRel } from '../soc-models';
import { toCytoscapeElements } from '../evidence-mapping';

const DEFAULT_COLOR = '#94a3b8';
const NODE_COLORS: Record<string, string> = {
  Service: '#2563eb',
  Endpoint: '#0891b2',
  Asset: '#0d9488',
  Role: '#7c3aed',
  User: '#db2777',
  Event: '#ca8a04',
  Alert: '#dc2626',
  Incident: '#b91c1c',
  Threat: '#ea580c',
  Control: '#16a34a',
  Vulnerability: '#9333ea',
  Document: '#475569',
};

const STYLE: StylesheetJson = [
  {
    selector: 'node',
    style: {
      label: 'data(label)',
      'background-color': (n: { data(k: string): string }) => NODE_COLORS[n.data('type')] ?? DEFAULT_COLOR,
      color: '#0f172a',
      'font-size': 10,
      'text-valign': 'bottom',
      'text-margin-y': 4,
      'text-wrap': 'ellipsis',
      'text-max-width': '90px',
    },
  },
  {
    selector: 'edge',
    style: {
      label: 'data(label)',
      width: 1.5,
      'line-color': '#94a3b8',
      'target-arrow-color': '#94a3b8',
      'target-arrow-shape': 'triangle',
      'curve-style': 'bezier',
      'font-size': 8,
      color: '#475569',
      'text-rotation': 'autorotate',
    },
  },
] as unknown as StylesheetJson;

// Cytoscape drawing of retrieved nodes/relationships. The library is loaded on first use so it
// stays out of the initial bundle.
@Component({
  selector: 'app-graph-view',
  templateUrl: './graph-view.html',
  styleUrl: './graph-view.css',
})
export class GraphView {
  readonly nodes = input.required<EvidenceNode[]>();
  readonly rels = input.required<EvidenceRel[]>();

  private readonly canvas = viewChild.required<ElementRef<HTMLElement>>('canvas');
  private cy: Core | null = null;
  private run = 0;
  private destroyed = false;

  constructor() {
    effect(() => {
      const elements = toCytoscapeElements(this.nodes(), this.rels());
      void this.draw(elements);
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.cy?.destroy();
      this.cy = null;
    });
  }

  private async draw(elements: ReturnType<typeof toCytoscapeElements>): Promise<void> {
    const run = ++this.run;
    const { default: cytoscape } = await import('cytoscape');
    if (this.destroyed || run !== this.run) return;
    this.cy?.destroy();
    this.cy = cytoscape({
      container: this.canvas().nativeElement,
      elements,
      style: STYLE,
      layout: { name: 'cose', animate: false, fit: true },
    });
  }
}
