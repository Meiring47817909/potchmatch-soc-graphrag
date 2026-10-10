import { Component, inject, input, output, signal, effect } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RagApi } from '../rag-api';
import { AskContext, RagAnswer, RagStatus } from '../soc-models';
import { describeHttpError } from '../http-error';

const MAX_QUESTION = 500;

const EXAMPLES = [
  'Why was this alert created and what should be done?',
  'What controls can mitigate this threat?',
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

  readonly question = input('');
  readonly context = input<AskContext>({});
  readonly answered = output<RagAnswer>();

  protected readonly maxLength = MAX_QUESTION;
  protected readonly examples = EXAMPLES;
  protected readonly text = signal('');
  protected readonly status = signal<RagStatus | null>(null);
  protected readonly statusError = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Alert/incident context applies only to the question it was set with; editing the text drops it.
  private readonly activeContext = signal<AskContext>({});

  constructor() {
    effect(() => {
      this.text.set(this.question());
      this.activeContext.set(this.context());
    });
    this.api.status().subscribe({
      next: (s) => this.status.set(s),
      error: (err) => this.statusError.set(this.message(err)),
    });
  }

  protected get enabled(): boolean {
    return this.status()?.enabled === true;
  }

  protected edit(value: string): void {
    this.text.set(value);
    this.activeContext.set({});
  }

  protected useExample(example: string): void {
    this.edit(example);
  }

  protected ask(): void {
    const question = this.text().trim();
    if (!this.enabled || this.loading() || question.length === 0) return;
    this.loading.set(true);
    this.error.set(null);
    const ctx = this.activeContext();
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
