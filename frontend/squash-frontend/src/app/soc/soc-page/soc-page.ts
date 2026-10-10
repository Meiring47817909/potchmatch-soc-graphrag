import { Component, signal } from '@angular/core';
import { AlertsPanel } from '../alerts-panel/alerts-panel';
import { IncidentsPanel } from '../incidents-panel/incidents-panel';
import { AskAbout, AskContext } from '../soc-models';

// Admin-only SOC view: alerts + incidents. Panels emit askAbout; SocPage keeps the
// question/context signals that the Ask panel (Task 9) takes as inputs.
@Component({
  selector: 'app-soc-page',
  imports: [AlertsPanel, IncidentsPanel],
  templateUrl: './soc-page.html',
  styleUrl: './soc-page.css',
})
export class SocPage {
  readonly question = signal('');
  readonly context = signal<AskContext>({});

  protected onAskAbout(e: AskAbout): void {
    this.question.set(e.question);
    this.context.set({ alertId: e.alertId, incidentId: e.incidentId });
  }
}
