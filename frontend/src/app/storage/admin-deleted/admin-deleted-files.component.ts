import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { catchError, of } from 'rxjs';
import { StoredObjectResponse } from '../../core/models/file.models';
import { formatBytes } from '../../shared/format-bytes';
import { ToastService } from '../../shared/toast/toast.service';
import { FilesService } from '../services/files.service';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-admin-deleted-files',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './admin-deleted-files.component.html',
  styleUrl: './admin-deleted-files.component.scss',
})
export class AdminDeletedFilesComponent {
  private readonly filesService = inject(FilesService);
  private readonly toast = inject(ToastService);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly items = signal<StoredObjectResponse[]>([]);
  protected readonly page = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly totalPages = signal(1);

  constructor() {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.error.set(null);

    this.filesService
      .listDeleted({ name: '', tag: '', contentType: '', from: '', to: '', page: this.page(), pageSize: PAGE_SIZE })
      .pipe(
        catchError(() => {
          this.error.set('Could not load deleted files.');
          return of(null);
        }),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.totalPages.set(Math.max(1, Math.ceil(result.totalCount / PAGE_SIZE)));
        }
      });
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }
    this.page.set(page);
    this.reload();
  }

  protected hardDelete(item: StoredObjectResponse): void {
    if (!confirm(`Permanently delete "${item.originalName}"? This cannot be undone.`)) {
      return;
    }

    this.filesService.hardDelete(item.id).subscribe({
      next: () => {
        this.toast.success(`"${item.originalName}" was permanently deleted.`);
        this.reload();
      },
    });
  }

  protected readonly formatBytes = formatBytes;
}
