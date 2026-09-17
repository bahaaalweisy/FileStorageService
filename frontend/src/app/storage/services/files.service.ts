import { HttpClient, HttpErrorResponse, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { FileListQuery, FileListResponse, StoredObjectResponse } from '../../core/models/file.models';

export interface PreviewResult {
  supported: boolean;
  blob?: Blob;
  contentType?: string;
  reason?: string;
}

export interface DownloadResult {
  blob: Blob;
  filename: string;
}

@Injectable({ providedIn: 'root' })
export class FilesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/files`;

  list(query: FileListQuery): Observable<FileListResponse> {
    return this.http.get<FileListResponse>(this.base, { params: this.buildListParams(query) });
  }

  listDeleted(query: FileListQuery): Observable<FileListResponse> {
    return this.http.get<FileListResponse>(`${this.base}/deleted`, { params: this.buildListParams(query) });
  }

  private buildListParams(query: FileListQuery): HttpParams {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);

    if (query.name) params = params.set('name', query.name);
    if (query.tag) params = params.set('tag', query.tag);
    if (query.contentType) params = params.set('contentType', query.contentType);
    if (query.from) params = params.set('from', query.from);
    if (query.to) params = params.set('to', query.to);

    return params;
  }

  getMetadata(id: string): Observable<StoredObjectResponse> {
    return this.http.get<StoredObjectResponse>(`${this.base}/${id}`);
  }

  upload(file: File, tags: string[]): Observable<HttpEvent<StoredObjectResponse>> {
    const formData = new FormData();
    if (tags.length > 0) {
      formData.append('tags', tags.join(','));
    }
    formData.append('file', file, file.name);

    return this.http.post<StoredObjectResponse>(this.base, formData, {
      reportProgress: true,
      observe: 'events',
    });
  }

  download(id: string): Observable<DownloadResult> {
    return new Observable<DownloadResult>((subscriber) => {
      const sub = this.http
        .get(`${this.base}/${id}/download`, { observe: 'response', responseType: 'blob' })
        .subscribe({
          next: (response) => {
            subscriber.next({
              blob: response.body as Blob,
              filename: extractFilename(response.headers.get('content-disposition')) ?? 'download',
            });
            subscriber.complete();
          },
          error: (err) => subscriber.error(err),
        });
      return () => sub.unsubscribe();
    });
  }

  preview(id: string): Observable<PreviewResult> {
    return this.http
      .get(`${this.base}/${id}/preview`, { observe: 'response', responseType: 'blob' })
      .pipe(
        map(
          (response): PreviewResult => ({
            supported: true,
            blob: response.body ?? undefined,
            contentType: response.headers.get('content-type') ?? undefined,
          }),
        ),
        catchError((err: HttpErrorResponse) => {
          if (err.status === 415) {
            return of<PreviewResult>({ supported: false, reason: 'This file type does not support inline preview.' });
          }
          throw err;
        }),
      );
  }

  softDelete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  hardDelete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}/hard`);
  }
}

function extractFilename(contentDisposition: string | null): string | null {
  if (!contentDisposition) {
    return null;
  }

  const starMatch = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (starMatch) {
    return decodeURIComponent(starMatch[1]);
  }

  const plainMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return plainMatch ? plainMatch[1] : null;
}
