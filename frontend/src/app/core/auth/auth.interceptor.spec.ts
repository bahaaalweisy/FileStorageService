import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  it('does not attach an Authorization header when there is no token', () => {
    http.get('/api/files').subscribe();

    const req = httpMock.expectOne('/api/files');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('attaches the bearer token only to requests targeting the API base URL', () => {
    sessionStorage.setItem(
      'fileStorage.session',
      JSON.stringify({
        accessToken: 'secret-token',
        expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
        userId: 'u1',
        role: 'user',
        displayName: 'Demo User',
      }),
    );
    TestBed.inject(AuthService);

    http.get('/api/files').subscribe();
    const apiReq = httpMock.expectOne('/api/files');
    expect(apiReq.request.headers.get('Authorization')).toBe('Bearer secret-token');
    apiReq.flush({});
  });
});
