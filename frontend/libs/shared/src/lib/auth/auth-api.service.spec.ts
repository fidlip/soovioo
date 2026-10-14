import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { AuthApiService } from './auth-api.service';
import { TokenStorage } from './token-storage';

class FakeTokenStorage implements TokenStorage {
  private token: string | null = null;
  getToken = () => this.token;
  setToken = (token: string) => void (this.token = token);
  clearToken = () => void (this.token = null);
}

describe('AuthApiService', () => {
  let service: AuthApiService;
  let httpMock: HttpTestingController;
  let tokenStorage: FakeTokenStorage;

  beforeEach(() => {
    tokenStorage = new FakeTokenStorage();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: tokenStorage },
      ],
    });
    service = TestBed.inject(AuthApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('stores the token after a successful login', () => {
    service.login({ username: 'ucitel', password: 'ucitel123' }).subscribe();
    const req = httpMock.expectOne('http://test/api/auth/login');
    req.flush({ token: 'abc', username: 'ucitel', role: 'Teacher' });

    expect(tokenStorage.getToken()).toBe('abc');
    expect(service.isLoggedIn()).toBe(true);
  });

  it('clears the token on logout', () => {
    tokenStorage.setToken('abc');
    service.logout();
    expect(service.isLoggedIn()).toBe(false);
  });
});
