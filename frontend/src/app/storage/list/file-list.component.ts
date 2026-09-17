import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { FileListQuery, StoredObjectResponse } from '../../core/models/file.models';
import { formatBytes } from '../../shared/format-bytes';
import { ToastService } from '../../shared/toast/toast.service';
import { FilesService } from '../services/files.service';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-file-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './file-list.component.html',
  styleUrl: './file-list.component.scss',
})
export class FileListComponent {
  private readonly filesService = inject(FilesService);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AuthService);

  protected readonly nameFilter = signal('');
  protected readonly tagFilter = signal('');
  protected readonly contentTypeFilter = signal('');
  protected readonly fromFilter = signal('');
  protected readonly toFilter = signal('');
  protected readonly page = signal(1);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly rows = signal<StoredObjectResponse[]>([]);
  protected readonly totalCount = signal(0);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  private readonly queryChanges$ = new Subject<FileListQuery>();

  constructor() {
    this.queryChanges$
      .pipe(
        debounceTime(300),
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((query) =>
          this.filesService.list(query).pipe(
            catchError(() => {
              this.error.set('Could not load files. Please try again.');
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.rows.set(result.items);
          this.totalCount.set(result.totalCount);
        }
      });

    this.reload();
  }

  protected onFilterChanged(): void {
    this.page.set(1);
    this.reload();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }
    this.page.set(page);
    this.reload();
  }

  reload(): void {
    this.queryChanges$.next(this.currentQuery());
  }

  private currentQuery(): FileListQuery {
    return {
      name: this.nameFilter().trim(),
      tag: this.tagFilter().trim(),
      contentType: this.contentTypeFilter().trim(),
      from: this.fromFilter(),
      to: this.toFilter(),
      page: this.page(),
      pageSize: PAGE_SIZE,
    };
  }

  protected softDelete(row: StoredObjectResponse): void {
    if (!confirm(`Soft-delete "${row.originalName}"? The file content is preserved${this.auth.isAdmin() ? ' — find it under "Deleted files" below to hard-delete it.' : ' and can be reviewed by an admin.'}`)) {
      return;
    }

    this.filesService.softDelete(row.id).subscribe({
      next: () => {
        this.toast.success(`"${row.originalName}" was soft-deleted.`);
        this.rows.update((list) => list.filter((r) => r.id !== row.id));
        this.totalCount.update((count) => Math.max(0, count - 1));
      },
    });
  }

  protected download(row: StoredObjectResponse): void {
    this.filesService.download(row.id).subscribe({
      next: ({ blob, filename }) => {
        const objectUrl = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = objectUrl;
        anchor.download = filename;
        anchor.click();
        URL.revokeObjectURL(objectUrl);
      },
    });
  }

  protected readonly formatBytes = formatBytes;
}
