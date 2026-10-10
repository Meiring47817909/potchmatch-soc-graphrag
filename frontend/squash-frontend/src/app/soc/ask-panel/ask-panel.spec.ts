import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AskPanel } from './ask-panel';
import { RagAnswer, RagStatus } from '../soc-models';

const answer = (): RagAnswer => ({
  question: 'q',
  answered: true,
  answer: 'Because [ALT-1]',
  mode: 'text2cypher',
  retrieval: {
    method: 'm',
    model: 'g',
    attempts: 1,
    explanation: null,
    rowCount: 1,
    truncated: false,
    unverifiedCitations: [],
  },
  cypher: 'MATCH (a) RETURN a',
  parameters: {},
  nodes: [],
  relationships: [],
  rows: [],
  sources: [],
});

describe('AskPanel', () => {
  let fixture: ComponentFixture<AskPanel>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const textarea = () => root().querySelector('textarea') as HTMLTextAreaElement;
  const askButton = () => root().querySelector('button[type="submit"]') as HTMLButtonElement;

  async function setup(status: RagStatus, question = '', context = {}) {
    TestBed.configureTestingModule({
      imports: [AskPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AskPanel);
    fixture.componentRef.setInput('question', question);
    fixture.componentRef.setInput('context', context);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/api/rag/status')).flush(status);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('AskPanel_DisabledWhenRagDisabled', async () => {
    await setup({ enabled: false, model: null, reason: 'GEMINI_API_KEY is not set' });
    expect(root().textContent).toContain('GEMINI_API_KEY is not set');
    expect(root().textContent).toContain('Set GEMINI_API_KEY in backend/.env and restart rag-service');
    expect(textarea().disabled).toBe(true);
    expect(askButton().disabled).toBe(true);
  });

  it('AskPanel_PostsQuestionWithContext', async () => {
    await setup({ enabled: true, model: 'g', reason: null }, 'Why?', { alertId: 'ALT-1' });
    expect(textarea().value).toBe('Why?');
    expect(textarea().maxLength).toBe(500);
    let emitted: RagAnswer | undefined;
    fixture.componentInstance.answered.subscribe((a) => (emitted = a));
    askButton().click();
    const req = http.expectOne((r) => r.url.endsWith('/api/rag/ask'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ question: 'Why?', alertId: 'ALT-1', incidentId: undefined });
    req.flush(answer());
    await fixture.whenStable();
    expect(emitted?.answer).toBe('Because [ALT-1]');
  });

  it('AskPanel_ChipFillsQuestionAndDropsContextWhenEdited', async () => {
    await setup({ enabled: true, model: 'g', reason: null }, 'Why?', { alertId: 'ALT-1' });
    (root().querySelector('.chips button') as HTMLButtonElement).click();
    fixture.detectChanges();
    askButton().click();
    const req = http.expectOne((r) => r.url.endsWith('/api/rag/ask'));
    expect(req.request.body.alertId).toBeUndefined();
    expect(req.request.body.question.length).toBeGreaterThan(10);
    req.flush(answer());
  });

  it('AskPanel_ShowsServerMessageVerbatimOn503', async () => {
    await setup({ enabled: true, model: 'g', reason: null }, 'Why?');
    askButton().click();
    http
      .expectOne((r) => r.url.endsWith('/api/rag/ask'))
      .flush(
        { error: 'llm_unavailable', message: 'The language model is busy; try again in a minute.' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root().textContent).toContain('The language model is busy; try again in a minute.');
    expect(askButton().disabled).toBe(false);
  });
});
