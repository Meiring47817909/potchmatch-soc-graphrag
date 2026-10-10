import { Component, computed, input } from '@angular/core';
import { JsonPipe } from '@angular/common';
import { RagAnswer } from '../soc-models';
import { toTriples } from '../evidence-mapping';
import { GraphView } from '../graph-view/graph-view';

// Shows a RagAnswer with its evidence: retrieval method, graph, triples, Cypher and sources.
// All server data is bound as text; nothing is rendered as HTML.
@Component({
  selector: 'app-evidence-panel',
  imports: [GraphView, JsonPipe],
  templateUrl: './evidence-panel.html',
  styleUrl: './evidence-panel.css',
})
export class EvidencePanel {
  readonly answer = input.required<RagAnswer>();
  // True when the question was asked about a specific alert/incident.
  readonly hadContext = input(false);

  protected readonly triples = computed(() => toTriples(this.answer().nodes, this.answer().relationships));
  protected readonly hasGraph = computed(() => this.answer().nodes.length > 0);
  protected readonly hasParameters = computed(() => Object.keys(this.answer().parameters ?? {}).length > 0);
  // Failed queries return one {error, reason} row; show rows when there are no relationships.
  protected readonly showRows = computed(
    () => this.answer().relationships.length === 0 && this.answer().rows.length > 0,
  );
}
