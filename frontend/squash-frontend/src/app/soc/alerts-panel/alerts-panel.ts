import { Subscription } from 'rxjs';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { SocApi } from '../soc-api';
import { AskAbout, SocAlert, SocEvent } from '../soc-models';
import { describeHttpError } from '../http-error';
import { EventsTable } from '../events-table/events-table';
import { shortId } from '../short-id';

const REFRESH_MS = 30_000;
const RELATED_MAX = 200; // the events endpoint's page-size cap

// Alerts table with the 11 spec fields, status/severity filters, acknowledge/resolve and "Why?".
// Clicking a row expands it to show the full description, recommended action and related events.
@Component({
  selector: 'app-alerts-panel',
  imports: [DatePipe, EventsTable],
  templateUrl: './alerts-panel.html',
  styleUrl: './alerts-panel.css',
})
export class AlertsPanel {
  private readonly api = inject(SocApi);

  /** False while the panel's tab is hidden: no loading or polling. */
  readonly active = input(true);

  readonly askAbout = output<AskAbout>();

  protected readonly alerts = signal<SocAlert[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  private inflight?: Subscription;
  protected readonly status = signal('');
  protected readonly severity = signal('');
  protected readonly expanded = signal<string | null>(null);
  protected readonly related = signal<SocEvent[] | null>(null);
  protected readonly relatedError = signal<string | null>(null);
  private relatedReq?: Subscription;
  protected readonly shortId = shortId;

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
      this.relatedReq?.unsubscribe();
    });
  }

  protected setStatusFilter(value: string): void {
    this.status.set(value);
    this.load();
  }

  protected setSeverityFilter(value: string): void {
    this.severity.set(value);
    this.load();
  }

  protected load(): void {
    // Cancel the previous request so a stale response can never overwrite a newer one.
    this.inflight?.unsubscribe();
    this.loading.set(true);
    this.inflight = this.api.getAlerts({ status: this.status(), severity: this.severity() }).subscribe({
      next: (alerts) => {
        this.alerts.set(alerts);
        this.error.set(null);
        this.loading.set(false);
        const open = this.expanded();
        if (open && !alerts.some((a) => a.alertId === open)) this.expanded.set(null);
        else if (open) this.loadRelated(open);
      },
      error: (err) => {
        this.error.set(describeHttpError(err));
        this.loading.set(false);
      },
    });
  }

  protected toggle(alert: SocAlert): void {
    if (this.expanded() === alert.alertId) {
      this.relatedReq?.unsubscribe();
      this.expanded.set(null);
      return;
    }
    this.expanded.set(alert.alertId);
    this.related.set(null);
    this.loadRelated(alert.alertId);
  }

  // Keeps the previously shown events until the new response arrives (no flicker on refresh).
  private loadRelated(alertId: string): void {
    this.relatedReq?.unsubscribe();
    this.relatedReq = this.api.getEvents({ alertId, pageSize: RELATED_MAX }).subscribe({
      next: (events) => {
        this.related.set(events);
        this.relatedError.set(null);
      },
      error: (err) => this.relatedError.set(describeHttpError(err)),
    });
  }

  protected change(alert: SocAlert, status: 'ACKNOWLEDGED' | 'RESOLVED'): void {
    this.api.setStatus(alert.alertId, status).subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(describeHttpError(err)),
    });
  }

  protected why(alert: SocAlert): void {
    this.askAbout.emit({
      question: 'Why was this alert created and what should be done?',
      alertId: alert.alertId,
    });
  }
}
