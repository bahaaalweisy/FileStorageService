import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { StoredObjectResponse } from '../../core/models/file.models';
import { formatBytes } from '../../shared/format-bytes';
import { FilesService } from '../services/files.service';

type PreviewState = 'loading' | 'image' | 'pdf' | 'unsupported' | 'error';

@Component({
  selector: 'app-file-preview',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './file-preview.component.html',
  styleUrl: './file-preview.component.scss',
})
export class FilePreviewComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly filesService = inject(FilesService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly metadata = signal<StoredObjectResponse | null>(null);
  protected readonly state = signal<PreviewState>('loading');
  protected readonly unsupportedReason = signal('');
  protected readonly objectUrl = signal<string | null>(null);
  protected readonly trustedPdfUrl = signal<SafeResourceUrl | null>(null);

  private readonly fileId: string;

  constructor() {
    this.fileId = this.route.snapshot.paramMap.get('id')!;
    this.destroyRef.onDestroy(() => this.revokeObjectUrl());
    this.load();
  }

  private load(): void {
    this.filesService.getMetadata(this.fileId).subscribe({
      next: (metadata) => {
        this.metadata.set(metadata);
        this.loadPreview();
      },
      error: () => this.state.set('error'),
    });
  }

  private loadPreview(): void {
    this.filesService.preview(this.fileId).subscribe({
      next: (result) => {
        if (!result.supported || !result.blob) {
          this.unsupportedReason.set(result.reason ?? 'Preview is not available for this file type.');
          this.state.set('unsupported');
          return;
        }

        const url = URL.createObjectURL(result.blob);
        this.objectUrl.set(url);

        const isPdf = result.contentType?.startsWith('application/pdf') ?? false;
        if (isPdf) {
          this.trustedPdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
        }
        this.state.set(isPdf ? 'pdf' : 'image');
      },
      error: () => this.state.set('error'),
    });
  }

  protected download(): void {
    this.filesService.download(this.fileId).subscribe(({ blob, filename }) => {
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = filename;
      anchor.click();
      URL.revokeObjectURL(url);
    });
  }

  protected readonly formatBytes = formatBytes;

  private revokeObjectUrl(): void {
    const url = this.objectUrl();
    if (url) {
      URL.revokeObjectURL(url);
    }
  }
}
