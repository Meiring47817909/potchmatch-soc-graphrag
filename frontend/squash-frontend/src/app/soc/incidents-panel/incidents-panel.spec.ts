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
  priority: 2,
  openedAt: '2026-10-10T10:00:00Z',
  lastAlertAt: '2026-10-10T10:05:00Z',
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

  it('IncidentsPanel_ShowsEscalatedBadge', async () => {
    await setup([row({ escalated: true }), row({ incidentId: 'inc-2', escalated: false })]);
    expect(root().querySelectorAll('.badge-escalated').length).toBe(1);
  });
});
