import { HttpEventType } from '@angular/common/http';
import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FilesService } from '../services/files.service';
import { formatBytes } from '../../shared/format-bytes';
import { ToastService } from '../../shared/toast/toast.service';
import { UploadItem } from './upload-item.model';

const CLIENT_MAX_SIZE_BYTES = 200 * 1024 * 1024;

let nextClientId = 1;

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div
      class="dropzone"
      [class.dropzone--active]="dragActive()"
      role="button"
      tabindex="0"
      aria-label="Drop files here or press Enter to browse for files to upload"
      (click)="fileInput.click()"
      (keydown.enter)="fileInput.click()"
      (keydown.space)="fileInput.click(); $event.preventDefault()"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      <p><strong>Drag & drop</strong> files here, or click to browse.</p>
      <p class="hint">Max {{ maxSizeLabel }} per file (server-enforced).</p>
      <input
        #fileInput
        type="file"
        multiple
        class="visually-hidden"
        (change)="onFilesPicked($event)"
        aria-hidden="true"
        tabindex="-1"
      />
    </div>

    @if (items().length > 0) {
      <ul class="upload-list">
        @for (item of items(); track item.clientId) {
          <li class="upload-item">
            <div class="upload-item__info">
              <span class="upload-item__name">{{ item.file.name }}</span>
              <span class="upload-item__size">{{ formatBytes(item.file.size) }}</span>
            </div>

            @if (item.status === 'pending') {
              <input
                type="text"
                class="upload-item__tags"
                placeholder="tags (comma-separated, optional)"
                [(ngModel)]="item.tagsText"
                [attr.aria-label]="'Tags for ' + item.file.name"
              />
              <button type="button" (click)="startUpload(item)">Upload</button>
            }

            @if (item.status === 'uploading') {
              <div class="progress" role="progressbar" [attr.aria-valuenow]="item.progressPercent" aria-valuemin="0" aria-valuemax="100">
                <div class="progress__bar" [style.width.%]="item.progressPercent"></div>
              </div>
              <span class="upload-item__status">{{ item.progressPercent }}%</span>
            }

            @if (item.status === 'success') {
              <span class="upload-item__status upload-item__status--success">Uploaded</span>
            }

            @if (item.status === 'error') {
              <div class="upload-item__error">
                <span class="upload-item__status upload-item__status--error">{{ item.errorMessage }}</span>
                <button type="button" (click)="retry(item)">Retry</button>
                <p class="hint">
                  If the upload actually completed on the server before this error, retrying will
                  create a second copy — check the file list below before retrying.
                </p>
              </div>
            }

            <button type="button" class="upload-item__remove" (click)="remove(item)" aria-label="Remove from list">&times;</button>
          </li>
        }
      </ul>
    }
  `,
  styles: [`
    .dropzone {
      border: 2px dashed #b0b8c1;
      border-radius: 0.75rem;
      padding: 2rem;
      text-align: center;
      cursor: pointer;
      background: #fafbfc;
      transition: border-color 0.15s, background 0.15s;
    }
    .dropzone:focus-visible { outline: 3px solid #1565c0; outline-offset: 2px; }
    .dropzone--active { border-color: #1565c0; background: #eef4fc; }
    .hint { color: #888; font-size: 0.8rem; margin: 0.25rem 0 0; }
    .visually-hidden {
      position: absolute;
      width: 1px; height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
    .upload-list { list-style: none; margin: 1rem 0 0; padding: 0; display: flex; flex-direction: column; gap: 0.5rem; }
    .upload-item {
      display: grid;
      grid-template-columns: 1fr auto auto auto;
      align-items: center;
      gap: 0.5rem;
      padding: 0.6rem 0.75rem;
      border: 1px solid #e2e6ea;
      border-radius: 0.5rem;
    }
    .upload-item__tags {
      padding: 0.35rem 0.5rem;
      border: 1px solid #d0d7de;
      border-radius: 0.375rem;
      font-size: 0.85rem;
      width: 12rem;
    }
    .upload-item__info { display: flex; flex-direction: column; min-width: 0; }
    .upload-item__name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .upload-item__size { font-size: 0.75rem; color: #888; }
    .progress { grid-column: 1 / -1; height: 6px; background: #eee; border-radius: 3px; overflow: hidden; }
    .progress__bar { height: 100%; background: #1565c0; transition: width 0.2s; }
    .upload-item__status { font-size: 0.8rem; }
    .upload-item__status--success { color: #1e7e34; }
    .upload-item__status--error { color: #c62828; }
    .upload-item__error { grid-column: 1 / -1; display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; }
    .upload-item__remove { background: none; border: none; cursor: pointer; font-size: 1.1rem; color: #888; }
  `],
})
export class UploadComponent {
  private readonly filesService = inject(FilesService);
  private readonly toast = inject(ToastService);

  @Output() readonly uploaded = new EventEmitter<void>();

  protected readonly items = signal<UploadItem[]>([]);
  protected readonly dragActive = signal(false);
  protected readonly maxSizeLabel = formatBytes(CLIENT_MAX_SIZE_BYTES);

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    const files = event.dataTransfer?.files;
    if (files) {
      this.enqueueFiles(files);
    }
  }

  onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files) {
      this.enqueueFiles(input.files);
    }
    input.value = '';
  }

  protected retry(item: UploadItem): void {
    this.startUpload(item);
  }

  remove(item: UploadItem): void {
    this.items.update((list) => list.filter((i) => i.clientId !== item.clientId));
  }

  private enqueueFiles(fileList: FileList): void {
    const newItems: UploadItem[] = Array.from(fileList).map((file) => ({
      clientId: `u${nextClientId++}`,
      file,
      tagsText: '',
      status: 'pending',
      progressPercent: 0,
    }));

    this.items.update((list) => [...list, ...newItems]);

    for (const item of newItems) {
      if (item.file.size > CLIENT_MAX_SIZE_BYTES) {
        this.updateItem(item.clientId, {
          status: 'error',
          errorMessage: `File exceeds the ${this.maxSizeLabel} client-side check.`,
        });
      }
    }
  }

  protected startUpload(item: UploadItem): void {
    if (item.status === 'uploading') {
      return;
    }

    this.updateItem(item.clientId, { status: 'uploading', progressPercent: 0, errorMessage: undefined });

    const tags = item.tagsText
      .split(',')
      .map((t) => t.trim())
      .filter((t) => t.length > 0);

    this.filesService.upload(item.file, tags).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          const percent = Math.round((event.loaded / event.total) * 100);
          this.updateItem(item.clientId, { progressPercent: percent });
        } else if (event.type === HttpEventType.Response) {
          this.updateItem(item.clientId, { status: 'success', progressPercent: 100 });
          this.toast.success(`${item.file.name} uploaded.`);
          this.uploaded.emit();
        }
      },
      error: (err) => {
        const message = err?.error?.detail ?? err?.error?.title ?? 'Upload failed.';
        this.updateItem(item.clientId, { status: 'error', errorMessage: message });
      },
    });
  }

  private updateItem(clientId: string, patch: Partial<UploadItem>): void {
    this.items.update((list) => list.map((i) => (i.clientId === clientId ? { ...i, ...patch } : i)));
  }

  protected readonly formatBytes = formatBytes;
}
