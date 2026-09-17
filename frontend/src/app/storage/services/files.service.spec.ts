import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FilesService } from './files.service';

describe('FilesService', () => {
  let service: FilesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FilesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() sends page/pageSize and only the non-empty filters as query params', () => {
    service
      .list({ name: 'invoice', tag: '', contentType: '', from: '', to: '', page: 2, pageSize: 10 })
      .subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/files' && r.params.get('name') === 'invoice' && r.params.get('page') === '2',
    );
    expect(req.request.params.has('tag')).toBeFalse();
    req.flush({ items: [], page: 2, pageSize: 10, totalCount: 0 });
  });

  it('download() extracts the filename from a UTF-8 Content-Disposition header', (done) => {
    service.download('abc').subscribe((result) => {
      expect(result.filename).toBe('café report.pdf');
      done();
    });

    const req = httpMock.expectOne('/api/files/abc/download');
    req.flush(new Blob(['x']), {
      headers: { 'content-disposition': "attachment; filename=\"file.pdf\"; filename*=UTF-8''caf%C3%A9%20report.pdf" },
    });
  });

  it('preview() maps a 415 response to an unsupported (non-throwing) result', (done) => {
    service.preview('abc').subscribe((result) => {
      expect(result.supported).toBeFalse();
      done();
    });

    const req = httpMock.expectOne('/api/files/abc/preview');
    req.flush(new Blob(['nope']), { status: 415, statusText: 'Unsupported Media Type' });
  });
});
