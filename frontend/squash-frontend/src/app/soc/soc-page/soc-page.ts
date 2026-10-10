import { Component, signal } from '@angular/core';
import { AlertsPanel } from '../alerts-panel/alerts-panel';
import { IncidentsPanel } from '../incidents-panel/incidents-panel';
import { AskPanel } from '../ask-panel/ask-panel';
import { EvidencePanel } from '../evidence-panel/evidence-panel';
import { AskAbout, AskContext, RagAnswer } from '../soc-models';

// Admin-only SOC view: alerts + incidents + Ask panel with its evidence. Panels emit askAbout;
// SocPage keeps the question/context signals the Ask panel takes as inputs and the latest answer.
@Component({
  selector: 'app-soc-page',
  imports: [AlertsPanel, IncidentsPanel, AskPanel, EvidencePanel],
  templateUrl: './soc-page.html',
  styleUrl: './soc-page.css',
})
export class SocPage {
  readonly question = signal('');
  readonly context = signal<AskContext>({});
  readonly answer = signal<RagAnswer | null>(null);

  protected onAskAbout(e: AskAbout): void {
    this.question.set(e.question);
    this.context.set({ alertId: e.alertId, incidentId: e.incidentId });
  }
}
