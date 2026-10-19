import { MessageService } from '@openng/optimus-ui/api';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL, TokenStorage } from '@soovioo/shared';
import { App } from './app';
import { appRoutes } from './app.routes';

class FakeTokenStorage extends TokenStorage {
  constructor(private token: string | null) {
    super();
  }
  getToken() {
    return this.token;
  }
  setToken(token: string) {
    this.token = token;
  }
  clearToken() {
    this.token = null;
  }
}

describe('App', () => {
  it('shows a login link when logged out', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        MessageService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(appRoutes),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: new FakeTokenStorage(null) },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Přihlásit se');
    expect(compiled.textContent).not.toContain('Známky');
  });

  it('shows feature navigation when logged in', async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        MessageService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(appRoutes),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: new FakeTokenStorage('fake-token') },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Známky');
    expect(compiled.textContent).toContain('Docházka');
    expect(compiled.textContent).toContain('Rozvrh');
    expect(compiled.textContent).toContain('Zprávy');
  });

  it('remembers a collapsed sidebar across reloads', async () => {
    localStorage.removeItem('soovioo-sidebar-collapsed');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        MessageService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(appRoutes),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: new FakeTokenStorage('fake-token') },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const sidebar = compiled.querySelector('aside') as HTMLElement;
    expect(sidebar.classList).not.toContain('collapsed');

    (
      compiled.querySelector('.app-collapse-toggle') as HTMLButtonElement
    ).click();
    fixture.detectChanges();

    expect(sidebar.classList).toContain('collapsed');
    expect(localStorage.getItem('soovioo-sidebar-collapsed')).toBe('true');
    localStorage.removeItem('soovioo-sidebar-collapsed');
  });
});
