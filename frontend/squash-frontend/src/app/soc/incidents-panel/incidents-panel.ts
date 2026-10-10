import { Subscription } from 'rxjs';
import { Component, DestroyRef, inject, output, signal } from '@angular/core';
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

  readonly askAbout = output<AskAbout>();

  protected readonly incidents = signal<GraphIncident[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  private inflight?: Subscription;
  protected readonly status = signal('');

  constructor() {
    this.load();
    const timer = setInterval(() => this.load(), REFRESH_MS);
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
