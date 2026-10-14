import { Component, signal } from '@angular/core';
import { EventsPanel } from '../events-panel/events-panel';
import { AlertsPanel } from '../alerts-panel/alerts-panel';
import { IncidentsPanel } from '../incidents-panel/incidents-panel';
import { AskPanel } from '../ask-panel/ask-panel';
import { EvidencePanel } from '../evidence-panel/evidence-panel';
import { AskAbout, AskContext, RagAnswer } from '../soc-models';

export type SocTab = 'events' | 'alerts' | 'incidents';

// Admin-only SOC view: Events / Alerts / Incidents tabs above the Ask panel with its evidence.
// Tabs are hidden rather than destroyed so each keeps its filters while you switch. Panels emit askAbout;
// SocPage keeps the question/context signals the Ask panel takes as inputs and the latest answer.
@Component({
  selector: 'app-soc-page',
  imports: [EventsPanel, AlertsPanel, IncidentsPanel, AskPanel, EvidencePanel],
  templateUrl: './soc-page.html',
  styleUrl: './soc-page.css',
})
export class SocPage {
  readonly tab = signal<SocTab>('alerts');
  readonly question = signal('');
  readonly context = signal<AskContext>({});
  readonly answer = signal<RagAnswer | null>(null);
  // Whether the latest question carried an alert/incident id (drives the sync hint on unanswered results).
  readonly askedWithContext = signal(false);

  protected onAsking(ctx: AskContext): void {
    this.answer.set(null);
    this.askedWithContext.set(!!(ctx.alertId || ctx.incidentId));
  }

  protected onAskAbout(e: AskAbout): void {
    this.question.set(e.question);
    this.context.set({ alertId: e.alertId, incidentId: e.incidentId });
  }
}
