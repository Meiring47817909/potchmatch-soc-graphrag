import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { IncidentsPanel } from './incidents-panel';
import { AskAbout } from '../soc-models';

const row = (over: Record<string, unknown> = {}) => ({
  incidentId: 'inc-1',
  title: 'Brute force then token abuse',
  subject: 'u-1',
  severity: 'HIGH',
  status: 'OPEN',
  escalated: false,
  priority: 'P2',
  openedAt: '2026-10-10T10:00:00Z',
  lastAlertAt: '2026-10-10T10:05:00Z',
  rules: ['GRAPH_ACCESS', 'Unauthorised Endpoint Access'],
  ...over,
});

describe('IncidentsPanel', () => {
  let fixture: ComponentFixture<IncidentsPanel>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  async function setup(rows: unknown[]) {
    TestBed.configureTestingModule({
      imports: [IncidentsPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IncidentsPanel);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url.endsWith('/api/graph/incidents'))
      .flush({ query: 'incidents', cypher: '', parameters: {}, nodes: [], relationships: [], rows });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('IncidentsPanel_AskEmitsIncidentQuestion', async () => {
    await setup([row()]);
    const emitted: AskAbout[] = [];
    fixture.componentInstance.askAbout.subscribe((e) => emitted.push(e));
    (root().querySelector('button.ask') as HTMLButtonElement).click();
    expect(emitted).toEqual([
      {
        question:
          'What does the graph know about this incident, its affected services and possible response actions?',
        incidentId: 'inc-1',
      },
    ]);
  });

  it('IncidentsPanel_CancelsStaleRequestWhenReloading', async () => {
    await setup([row()]);
    const component = fixture.componentInstance as unknown as { load(): void; setStatusFilter(v: string): void };
    component.load(); // auto-refresh in flight
    const stale = http.expectOne((r) => r.url.endsWith('/api/graph/incidents'));
    component.setStatusFilter('OPEN');
    const fresh = http.expectOne((r) => r.url.endsWith('/api/graph/incidents'));
    expect(stale.cancelled).toBe(true);
    fresh.flush({ query: 'incidents', cypher: '', parameters: {}, nodes: [], relationships: [], rows: [row({ title: 'Fresh one' })] });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root().textContent).toContain('Fresh one');
  });

  it('IncidentsPanel_ShowsEscalatedBadge', async () => {
    await setup([row({ escalated: true }), row({ incidentId: 'inc-2', escalated: false })]);
    expect(root().querySelectorAll('.badge-escalated').length).toBe(1);
  });

  it('IncidentsPanel_ShowsRuleColumnAfterIncident', async () => {
    await setup([row(), row({ incidentId: 'inc-2', rules: [] })]);
    const headers = Array.from(root().querySelectorAll('thead th')).map((th) => th.textContent!.trim());
    expect(headers.slice(0, 2)).toEqual(['Incident', 'Rule']);
    const rows = root().querySelectorAll('tbody tr');
    const rules = Array.from(rows[0].querySelectorAll('td.rules .rule')).map((r) => r.textContent!.trim());
    expect(rules).toEqual(['GRAPH_ACCESS', 'Unauthorised Endpoint Access']);
    expect(rows[1].querySelector('td.rules')!.textContent!.trim()).toBe('-');
  });
});
