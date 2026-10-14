import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SocPage } from './soc-page';

describe('SocPage', () => {
  it('stores question and context from a panel askAbout event', async () => {
    TestBed.configureTestingModule({
      imports: [SocPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(SocPage);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).match(() => true);
    (fixture.componentInstance as unknown as { onAskAbout(e: unknown): void }).onAskAbout({
      question: 'q',
      alertId: 'a-1',
    });
    expect(fixture.componentInstance.question()).toBe('q');
    expect(fixture.componentInstance.context()).toEqual({ alertId: 'a-1', incidentId: undefined });
  });

  it('switches between the Events, Alerts and Incidents tabs', async () => {
    TestBed.configureTestingModule({
      imports: [SocPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(SocPage);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).match(() => true);
    const root = fixture.nativeElement as HTMLElement;
    const hidden = (sel: string) => (root.querySelector(sel) as HTMLElement).hidden;

    expect(root.querySelector('.tabs')!.textContent).toContain('Incidents (from the knowledge graph)');
    expect(hidden('app-alerts-panel')).toBe(false);
    expect(hidden('app-events-panel')).toBe(true);

    (root.querySelector('button.tab-events') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(hidden('app-events-panel')).toBe(false);
    expect(hidden('app-alerts-panel')).toBe(true);
    expect(hidden('app-incidents-panel')).toBe(true);

    (root.querySelector('button.tab-incidents') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(hidden('app-incidents-panel')).toBe(false);
    expect(root.querySelector('app-ask-panel')).not.toBeNull();
  });

  it('loads a hidden tab only when it is opened', async () => {
    TestBed.configureTestingModule({
      imports: [SocPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(SocPage);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    expect(http.match((r) => r.url.endsWith('/api/soc/events')).length).toBe(0);
    expect(http.match((r) => r.url.endsWith('/api/soc/alerts')).length).toBe(1);
    http.match(() => true);

    (fixture.nativeElement.querySelector('button.tab-events') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(http.match((r) => r.url.endsWith('/api/soc/events')).length).toBe(1);
  });
});
