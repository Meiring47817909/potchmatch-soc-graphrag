import { Subscription } from 'rxjs';
import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { SocApi } from '../soc-api';
import { SocEvent } from '../soc-models';
import { describeHttpError } from '../http-error';
import { EventsTable } from '../events-table/events-table';

const REFRESH_MS = 30_000;
export const EVENTS_PAGE_SIZE = 50;
export const NOISY_TYPE = 'REQUEST_RECEIVED';

// Events tab: the stored SOC events with type/user filters, paging and a "hide REQUEST_RECEIVED" toggle
// (ignored when REQUEST_RECEIVED itself is the chosen type).
@Component({
  selector: 'app-events-panel',
  imports: [EventsTable],
  templateUrl: './events-panel.html',
  styleUrl: './events-panel.css',
})
export class EventsPanel {
  private readonly api = inject(SocApi);

  /** False while the panel's tab is hidden: no loading or polling. */
  readonly active = input(true);

  protected readonly events = signal<SocEvent[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly type = signal('');
  protected readonly user = signal('');
  protected readonly hideNoise = signal(true);
  protected readonly page = signal(1);
  protected readonly pageSize = EVENTS_PAGE_SIZE;
  private inflight?: Subscription;

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

  protected setType(value: string): void {
    this.type.set(value.trim());
    this.page.set(1);
    this.load();
  }

  protected setUser(value: string): void {
    this.user.set(value.trim());
    this.page.set(1);
    this.load();
  }

  protected setHideNoise(value: boolean): void {
    this.hideNoise.set(value);
    this.page.set(1);
    this.load();
  }

  protected goTo(page: number): void {
    this.page.set(Math.max(1, page));
    this.load();
  }

  protected load(): void {
    this.inflight?.unsubscribe();
    this.loading.set(true);
    this.inflight = this.api
      .getEvents({
        type: this.type(),
        user: this.user(),
        excludeType: this.hideNoise() && this.type() !== NOISY_TYPE ? NOISY_TYPE : undefined,
        page: this.page(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (events) => {
          this.events.set(events);
          this.error.set(null);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(describeHttpError(err));
          this.loading.set(false);
        },
      });
  }
}
