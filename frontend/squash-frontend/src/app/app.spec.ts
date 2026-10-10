import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { Auth } from './services/auth';

describe('App', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  const login = (role: string) =>
    TestBed.inject(Auth).currentPlayer.set({
      playerId: 'p-1',
      name: 'Test',
      surname: 'User',
      role,
      clubId: 'c-1',
    });

  it('should create the app', () => {
    expect(TestBed.createComponent(App).componentInstance).toBeTruthy();
  });

  it('SocButton_OnlyForAdmin: a Player sees no SOC button', async () => {
    const fixture = TestBed.createComponent(App);
    login('Player');
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('button.soc-button')).toBeNull();
    expect(el.querySelector('app-soc-page')).toBeNull();
  });

  it('SocButton_OnlyForAdmin: an Admin gets the button and lands on the SOC view', async () => {
    const fixture = TestBed.createComponent(App);
    login('Admin');
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('button.soc-button')).not.toBeNull();
    expect(el.querySelector('app-soc-page')).not.toBeNull();
    http.match(() => true);
  });
});
