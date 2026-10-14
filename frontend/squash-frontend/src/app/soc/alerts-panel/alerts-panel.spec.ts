import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AlertsPanel } from './alerts-panel';
import { AskAbout, SocAlert, SocEvent } from '../soc-models';

const alert = (over: Partial<SocAlert> = {}): SocAlert => ({
  alertId: 'a-1',
  timestamp: '2026-10-10T10:00:00Z',
  ruleName: 'BRUTE_FORCE_LOGIN',
  severity: 'HIGH',
  status: 'OPEN',
  description: 'Five failed logins',
  relatedEvents: [],
  affectedService: 'identity-service',
  affectedUser: 'u-1',
  affectedEntity: null,
  recommendedAction: 'Lock the account',
  ...over,
});

describe('AlertsPanel', () => {
  let fixture: ComponentFixture<AlertsPanel>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  async function setup(alerts: SocAlert[] = [alert()]) {
    TestBed.configureTestingModule({
      imports: [AlertsPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AlertsPanel);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/api/soc/alerts')).flush(alerts);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('AlertsPanel_RendersRowsAndFilters', async () => {
    await setup();
    expect(root().querySelectorAll('tbody tr').length).toBe(1);
    expect(root().textContent).toContain('BRUTE_FORCE_LOGIN');

    const select = root().querySelector('select[name="status"]') as HTMLSelectElement;
    select.value = 'OPEN';
    select.dispatchEvent(new Event('change'));
    const req = http.expectOne((r) => r.url.endsWith('/api/soc/alerts'));
    expect(req.request.params.get('status')).toBe('OPEN');
    req.flush([alert()]);
  });

  it('AlertsPanel_CancelsStaleRequestWhenReloading', async () => {
    await setup();
    const component = fixture.componentInstance as unknown as { load(): void };
    component.load(); // auto-refresh in flight
    const stale = http.expectOne((r) => r.url.endsWith('/api/soc/alerts'));
    const select = root().querySelector('select[name="status"]') as HTMLSelectElement;
    select.value = 'OPEN';
    select.dispatchEvent(new Event('change'));
    const fresh = http.expectOne((r) => r.url.endsWith('/api/soc/alerts'));
    expect(stale.cancelled).toBe(true);
    fresh.flush([alert({ ruleName: 'NEW_RULE_X' })]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root().textContent).toContain('NEW_RULE_X');
  });

  it('AlertsPanel_AcknowledgeCallsPatch', async () => {
    await setup();
    (root().querySelector('button.ack') as HTMLButtonElement).click();
    const patch = http.expectOne((r) => r.method === 'PATCH');
    expect(patch.request.url).toMatch(/\/api\/soc\/alerts\/a-1\/status$/);
    expect(patch.request.body).toEqual({ status: 'ACKNOWLEDGED' });
    patch.flush(alert({ status: 'ACKNOWLEDGED' }));
    http.expectOne((r) => r.method === 'GET').flush([alert({ status: 'ACKNOWLEDGED' })]);
  });

  it('AlertsPanel_WhyEmitsAskAbout', async () => {
    await setup();
    const emitted: AskAbout[] = [];
    fixture.componentInstance.askAbout.subscribe((e) => emitted.push(e));
    (root().querySelector('button.why') as HTMLButtonElement).click();
    expect(emitted).toEqual([
      { question: 'Why was this alert created and what should be done?', alertId: 'a-1' },
    ]);
  });

  it('AlertsPanel_401_ShowsSessionExpired', async () => {
    TestBed.configureTestingModule({
      imports: [AlertsPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AlertsPanel);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url.endsWith('/api/soc/alerts'))
      .flush('no', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root().textContent).toContain('Session expired — please log in again');
  });

  it('AlertsPanel_ShowsExactlyTheElevenSpecColumns', async () => {
    await setup([alert({ relatedEvents: ['e-1', 'e-2'] })]);
    const headers = Array.from(root().querySelectorAll('thead th'))
      .map((th) => th.textContent!.trim())
      .filter((t) => t.length > 0);
    expect(headers).toEqual([
      'alertId', 'timestamp', 'ruleName', 'severity', 'status', 'description',
      'relatedEvents', 'affectedService', 'affectedUser', 'affectedEntity', 'recommendedAction',
    ]);
    const cells = root().querySelectorAll('tbody tr.alert-row td');
    expect(cells[6].textContent!.trim()).toBe('2');
    expect(cells[10].textContent).toContain('Lock the account');
  });

  it('AlertsPanel_RowClickExpandsAndLoadsRelatedEvents', async () => {
    await setup([alert({ relatedEvents: ['e-1'] })]);
    (root().querySelector('tr.alert-row') as HTMLElement).click();
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url.endsWith('/api/soc/events'));
    expect(req.request.params.get('alertId')).toBe('a-1');
    const ev: SocEvent = {
      eventId: 'e-1', timestamp: '2026-10-10T09:59:00Z', serviceName: 'identity-service',
      eventType: 'FAILED_LOGIN', severity: 'MEDIUM', userId: 'u-1', sourceIp: '10.0.0.9',
      endpoint: '/api/auth/login', httpMethod: 'POST', statusCode: 401, message: 'Bad password',
      correlationId: 'c-1', affectedEntity: null,
    };
    req.flush([ev]);
    await fixture.whenStable();
    fixture.detectChanges();
    const detail = root().querySelector('tr.detail') as HTMLElement;
    expect(detail.textContent).toContain('Lock the account');
    expect(detail.querySelectorAll('app-events-table thead th').length).toBe(13);
    expect(detail.textContent).toContain('Bad password');

    (root().querySelector('tr.alert-row') as HTMLElement).click();
    fixture.detectChanges();
    expect(root().querySelector('tr.detail')).toBeNull();
  });

  it('AlertsPanel_ActionButtonsDoNotToggleRow', async () => {
    await setup();
    const emitted: AskAbout[] = [];
    fixture.componentInstance.askAbout.subscribe((e) => emitted.push(e));
    (root().querySelector('button.why') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root().querySelector('tr.detail')).toBeNull();
    expect(emitted.length).toBe(1);
  });
});
