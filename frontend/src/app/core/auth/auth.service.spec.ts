import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('starts unauthenticated when there is no stored session', () => {
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.getToken()).toBeNull();
  });

  it('login() stores the token and exposes role/admin state', async () => {
    const loginPromise = service.login('admin');

    const req = httpMock.expectOne('/api/auth/mock-token');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ role: 'admin' });

    req.flush({
      accessToken: 'fake-token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 'admin-id',
      role: 'admin',
      displayName: 'Demo Admin',
    });

    await loginPromise;

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.isAdmin()).toBeTrue();
    expect(service.getToken()).toBe('fake-token');
    expect(sessionStorage.getItem('fileStorage.session')).toContain('fake-token');
  });

  it('logout() clears the session', async () => {
    const loginPromise = service.login('user');
    httpMock.expectOne('/api/auth/mock-token').flush({
      accessToken: 'fake-token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-id',
      role: 'user',
      displayName: 'Demo User',
    });
    await loginPromise;

    service.logout();

    expect(service.isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem('fileStorage.session')).toBeNull();
  });
});
