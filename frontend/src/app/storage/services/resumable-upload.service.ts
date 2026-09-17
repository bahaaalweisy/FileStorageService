import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { StoredObjectResponse } from '../../core/models/file.models';

export interface UploadSessionStatus {
  sessionId: string;
  status: 'InProgress' | 'Completed' | 'Aborted' | 'Expired';
  totalSizeBytes: number;
  receivedBytes: number;
  nextExpectedOffset: number;
  expiresAtUtc: string;
  finalizedFileId: string | null;
}

@Injectable({ providedIn: 'root' })
export class ResumableUploadService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/upload-sessions`;

  readonly chunkSizeBytes = 2 * 1024 * 1024;

  async createSession(file: File, tags: string[]): Promise<UploadSessionStatus> {
    return firstValueFrom(
      this.http.post<UploadSessionStatus>(this.base, {
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        totalSizeBytes: file.size,
        tags,
      }),
    );
  }

  async getStatus(sessionId: string): Promise<UploadSessionStatus> {
    return firstValueFrom(this.http.get<UploadSessionStatus>(`${this.base}/${sessionId}`));
  }

  async uploadChunks(
    sessionId: string,
    file: File,
    startOffset: number,
    onProgress: (receivedBytes: number, totalBytes: number) => void,
    cancelledCheck: () => boolean,
  ): Promise<void> {
    let offset = startOffset;

    while (offset < file.size) {
      if (cancelledCheck()) {
        return;
      }

      const end = Math.min(offset + this.chunkSizeBytes, file.size);
      const chunk = file.slice(offset, end);

      const status = await firstValueFrom(
        this.http.put<UploadSessionStatus>(`${this.base}/${sessionId}/chunks?offset=${offset}`, chunk),
      );

      onProgress(status.receivedBytes, file.size);
      offset = status.receivedBytes;
    }
  }

  async finalize(sessionId: string): Promise<StoredObjectResponse> {
    return firstValueFrom(this.http.post<StoredObjectResponse>(`${this.base}/${sessionId}/finalize`, null));
  }

  async abort(sessionId: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${sessionId}`));
  }
}
