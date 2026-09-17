export type UploadStatus = 'pending' | 'uploading' | 'success' | 'error';

export interface UploadItem {
  clientId: string;
  file: File;
  tagsText: string;
  status: UploadStatus;
  progressPercent: number;
  errorMessage?: string;

  resumable?: boolean;
  resumableSessionId?: string;
  resumableReceivedBytes?: number;
}
