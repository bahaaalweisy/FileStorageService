import { Component, inject } from '@angular/core';
import { AuthService } from '../core/auth/auth.service';
import { AdminDeletedFilesComponent } from './admin-deleted/admin-deleted-files.component';
import { FileListComponent } from './list/file-list.component';
import { UploadComponent } from './upload/upload.component';

@Component({
  selector: 'app-storage-page',
  standalone: true,
  imports: [UploadComponent, FileListComponent, AdminDeletedFilesComponent],
  template: `
    <section class="panel">
      <h2>Upload</h2>
      <app-upload (uploaded)="fileList.reload()" />
    </section>

    <section class="panel">
      <h2>Your files</h2>
      <app-file-list #fileList />
    </section>

    @if (auth.isAdmin()) {
      <section class="panel">
        <h2>Deleted files</h2>
        <p class="panel-hint">
          Soft-deleted files, server-backed — this list survives page refreshes and new sessions.
          Hard delete here to permanently remove one.
        </p>
        <app-admin-deleted-files />
      </section>
    }
  `,
  styles: [`
    .panel {
      background: #fff;
      border-radius: 0.75rem;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
      padding: 1.5rem;
      margin-bottom: 1.5rem;
    }
    h2 { margin-top: 0; font-size: 1.1rem; }
    .panel-hint { color: #777; font-size: 0.8rem; margin-top: -0.5rem; }
  `],
})
export class StoragePageComponent {
  protected readonly auth = inject(AuthService);
}
