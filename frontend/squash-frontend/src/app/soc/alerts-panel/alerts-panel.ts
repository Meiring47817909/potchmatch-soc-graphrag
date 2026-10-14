import { Subscription } from 'rxjs';
import { Component, DestroyRef, inject, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { SocApi } from '../soc-api';
import { AskAbout, SocAlert, SocEvent } from '../soc-models';
import { describeHttpError } from '../http-error';
import { EventsTable } from '../events-table/events-table';
import { shortId } from '../short-id';

const REFRESH_MS = 30_000;

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
    this.load();
    const timer = setInterval(() => this.load(), REFRESH_MS);
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
      },
      error: (err) => {
        this.error.set(describeHttpError(err));
        this.loading.set(false);
      },
    });
  }

  protected toggle(alert: SocAlert): void {
    this.relatedReq?.unsubscribe();
    if (this.expanded() === alert.alertId) {
      this.expanded.set(null);
      return;
    }
    this.expanded.set(alert.alertId);
    this.related.set(null);
    this.relatedError.set(null);
    this.relatedReq = this.api.getEvents({ alertId: alert.alertId, pageSize: 200 }).subscribe({
      next: (events) => this.related.set(events),
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
