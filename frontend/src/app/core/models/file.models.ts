export interface StoredObjectResponse {
  id: string;
  key: string;
  originalName: string;
  sizeBytes: number;
  contentType: string;
  checksum: string;
  tags: string[];
  createdAtUtc: string;
  deletedAtUtc: string | null;
  version: number;
  createdByUserId: string;
}

export interface FileListResponse {
  items: StoredObjectResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface FileListFilters {
  name: string;
  tag: string;
  contentType: string;
  from: string;
  to: string;
}

export interface FileListQuery extends FileListFilters {
  page: number;
  pageSize: number;
}

export interface PreviewUnsupportedResponse {
  supported: false;
  reason: string;
}
