import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login.component').then((m) => m.LoginComponent) },
  {
    path: 'files',
    canActivate: [authGuard],
    loadComponent: () => import('./storage/storage-page.component').then((m) => m.StoragePageComponent),
  },
  {
    path: 'files/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./storage/preview/file-preview.component').then((m) => m.FilePreviewComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'files' },
  { path: '**', redirectTo: 'files' },
];
