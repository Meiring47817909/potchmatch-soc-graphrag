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
});
