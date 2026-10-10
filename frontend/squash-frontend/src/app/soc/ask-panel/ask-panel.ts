import { Component, DestroyRef, inject, input, output, signal, effect } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RagApi } from '../rag-api';
import { AskContext, RagAnswer, RagStatus } from '../soc-models';
import { describeHttpError } from '../http-error';

const MAX_QUESTION = 500;
const STATUS_ATTEMPTS = 3;
const STATUS_RETRY_MS = 5000;

const EXAMPLES = [
  'What controls can mitigate brute force attacks?',
  'What is the likely impact if payment-service fails?',
  'Which users triggered high severity alerts?',
  'Which alerts affect identity-service?',
];

// Ask box: question text, example chips, and the call to RagService. Holds no evidence;
// the latest RagAnswer is emitted for the evidence panel.
@Component({
  selector: 'app-ask-panel',
  templateUrl: './ask-panel.html',
  styleUrl: './ask-panel.css',
})
export class AskPanel {
  private readonly api = inject(RagApi);
  private readonly destroyRef = inject(DestroyRef);
  private statusTimer: ReturnType<typeof setTimeout> | undefined;

  readonly question = input('');
  readonly context = input<AskContext>({});
  readonly answered = output<RagAnswer>();
  // Emits the alert/incident context the question is asked with (for the evidence hint).
  readonly asking = output<AskContext>();

  protected readonly maxLength = MAX_QUESTION;
  protected readonly examples = EXAMPLES;
  protected readonly text = signal('');
  protected readonly status = signal<RagStatus | null>(null);
  protected readonly statusError = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Alert/incident context survives free-text edits; it is cleared by the pill, an example chip,
  // or when a panel's Why?/Ask replaces it.
  private readonly activeContext = signal<AskContext>({});

  constructor() {
    effect(() => {
      this.text.set(this.question());
      this.activeContext.set(this.context());
    });
    this.destroyRef.onDestroy(() => clearTimeout(this.statusTimer));
    this.loadStatus(1);
  }

  // Status is fetched on init; a failure is retried (3 attempts, 5 s apart) before the error shows.
  // The Retry button starts again, e.g. after the key was added and rag-service recreated.
  private loadStatus(attempt: number): void {
    clearTimeout(this.statusTimer);
    this.api.status().subscribe({
      next: (s) => {
        this.statusError.set(null);
        this.status.set(s);
      },
      error: (err) => {
        if (attempt < STATUS_ATTEMPTS) {
          this.statusTimer = setTimeout(() => this.loadStatus(attempt + 1), STATUS_RETRY_MS);
        } else {
          this.statusError.set(this.message(err));
        }
      },
    });
  }

  protected retryStatus(): void {
    this.statusError.set(null);
    this.loadStatus(1);
  }

  protected get enabled(): boolean {
    return this.status()?.enabled === true;
  }

  protected edit(value: string): void {
    this.text.set(value);
  }

  protected useExample(example: string): void {
    this.text.set(example);
    this.clearContext();
  }

  protected clearContext(): void {
    this.activeContext.set({});
  }

  protected contextLabel(): string | null {
    const c = this.activeContext();
    if (c.alertId) return `alert ${c.alertId}`;
    if (c.incidentId) return `incident ${c.incidentId}`;
    return null;
  }

  protected ask(): void {
    const question = this.text().trim();
    if (!this.enabled || this.loading() || question.length === 0) return;
    this.loading.set(true);
    this.error.set(null);
    const ctx = this.activeContext();
    this.asking.emit({ alertId: ctx.alertId, incidentId: ctx.incidentId });
    this.api.ask({ question, alertId: ctx.alertId, incidentId: ctx.incidentId }).subscribe({
      next: (a) => {
        this.loading.set(false);
        this.answered.emit(a);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(this.message(err));
      },
    });
  }

  // 400/503 bodies are {error, message}; show the server message verbatim.
  private message(err: unknown): string {
    if (err instanceof HttpErrorResponse && (err.status === 400 || err.status === 503)) {
      const m = (err.error as { message?: unknown } | null)?.message;
      if (typeof m === 'string' && m.length > 0) return m;
    }
    return describeHttpError(err);
  }
}
