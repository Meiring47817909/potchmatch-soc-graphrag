import { Subscription } from 'rxjs';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { GraphApi } from '../graph-api';
import { AskAbout, GraphIncident } from '../soc-models';
import { describeHttpError } from '../http-error';

const REFRESH_MS = 30_000;

// Incidents from the knowledge graph (GET /api/graph/incidents), with an "Ask" button per row.
@Component({
  selector: 'app-incidents-panel',
  imports: [DatePipe],
  templateUrl: './incidents-panel.html',
  styleUrl: './incidents-panel.css',
})
export class IncidentsPanel {
  private readonly api = inject(GraphApi);

  /** False while the panel's tab is hidden: no loading or polling. */
  readonly active = input(true);

  readonly askAbout = output<AskAbout>();

  protected readonly incidents = signal<GraphIncident[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  private inflight?: Subscription;
  protected readonly status = signal('');

  constructor() {
    // Load when the tab becomes visible; the 30 s refresh only runs while it is visible.
    effect(() => {
      if (this.active()) untracked(() => this.load());
    });
    const timer = setInterval(() => {
      if (this.active()) this.load();
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      this.inflight?.unsubscribe();
    });
  }

  protected setStatusFilter(value: string): void {
    this.status.set(value);
    this.load();
  }

  protected load(): void {
    // Cancel the previous request so a stale response can never overwrite a newer one.
    this.inflight?.unsubscribe();
    this.loading.set(true);
    this.inflight = this.api.getIncidents(this.status()).subscribe({
      next: (incidents) => {
        this.incidents.set(incidents);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(describeHttpError(err));
        this.loading.set(false);
      },
    });
  }

  protected ask(incident: GraphIncident): void {
    this.askAbout.emit({
      question:
        'What does the graph know about this incident, its affected services and possible response actions?',
      incidentId: incident.incidentId,
    });
  }
}
