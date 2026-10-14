import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { EventsPanel } from './events-panel';
import { SocEvent } from '../soc-models';

const ev = (over: Partial<SocEvent> = {}): SocEvent => ({
  eventId: '0f1e2d3c-aaaa-bbbb-cccc-000000000001',
  timestamp: '2026-10-10T10:00:00Z',
  serviceName: 'identity-service',
  eventType: 'FAILED_LOGIN',
  severity: 'MEDIUM',
  userId: 'u-1',
  sourceIp: '10.0.0.9',
  endpoint: '/api/auth/login',
  httpMethod: 'POST',
  statusCode: 401,
  message: 'Bad password',
  correlationId: 'corr-123456789',
  affectedEntity: 'user:u-1',
  ...over,
});

describe('EventsPanel', () => {
  let fixture: ComponentFixture<EventsPanel>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const nextReq = (): TestRequest => http.expectOne((r) => r.url.endsWith('/api/soc/events'));

  async function setup(events: SocEvent[] = [ev()]) {
    TestBed.configureTestingModule({
      imports: [EventsPanel],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EventsPanel);
    fixture.detectChanges();
    const first = nextReq();
    expect(first.request.params.get('excludeType')).toBe('REQUEST_RECEIVED');
    expect(first.request.params.get('page')).toBe('1');
    expect(first.request.params.get('pageSize')).toBe('50');
    first.flush(events);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('EventsPanel_ShowsExactlyTheThirteenSpecColumns', async () => {
    await setup();
    const headers = Array.from(root().querySelectorAll('thead th')).map((th) => th.textContent!.trim());
    expect(headers).toEqual([
      'eventId', 'timestamp', 'serviceName', 'eventType', 'severity', 'userId', 'sourceIp',
      'endpoint', 'httpMethod', 'statusCode', 'message', 'correlationId', 'affectedEntity',
    ]);
    const cells = Array.from(root().querySelectorAll('tbody tr td'));
    expect(cells.length).toBe(13);
    expect(cells[0].textContent!.trim()).toBe('0f1e2d3c…');
    expect(cells[0].getAttribute('title')).toBe('0f1e2d3c-aaaa-bbbb-cccc-000000000001');
    expect(cells[9].textContent!.trim()).toBe('401');
    expect(cells[12].textContent!.trim()).toBe('user:u-1');
  });

  it('EventsPanel_FiltersByTypeUserAndNoiseToggle', async () => {
    await setup();
    const type = root().querySelector('input[name="type"]') as HTMLInputElement;
    type.value = 'FAILED_LOGIN';
    type.dispatchEvent(new Event('change'));
    let req = nextReq();
    expect(req.request.params.get('type')).toBe('FAILED_LOGIN');
    req.flush([]);

    const user = root().querySelector('input[name="user"]') as HTMLInputElement;
    user.value = ' u-1 ';
    user.dispatchEvent(new Event('change'));
    req = nextReq();
    expect(req.request.params.get('user')).toBe('u-1');
    req.flush([]);

    const box = root().querySelector('input[name="hideNoise"]') as HTMLInputElement;
    box.checked = false;
    box.dispatchEvent(new Event('change'));
    req = nextReq();
    expect(req.request.params.has('excludeType')).toBe(false);
    req.flush([]);
  });

  it('EventsPanel_PagesWithNextAndPrevious', async () => {
    await setup(Array.from({ length: 50 }, (_, i) => ev({ eventId: `id-${i}` })));
    const prev = root().querySelector('button.prev') as HTMLButtonElement;
    const next = root().querySelector('button.next') as HTMLButtonElement;
    expect(prev.disabled).toBe(true);
    expect(next.disabled).toBe(false);
    next.click();
    const req = nextReq();
    expect(req.request.params.get('page')).toBe('2');
    req.flush([ev()]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(prev.disabled).toBe(false);
    expect(next.disabled).toBe(true);
  });
});
