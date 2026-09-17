import { Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  template: `
    <div class="toast-container" role="status" aria-live="polite">
      @for (toast of toastService.toasts(); track toast.id) {
        <div class="toast toast--{{ toast.kind }}">
          <span>{{ toast.message }}</span>
          <button type="button" class="toast__close" (click)="toastService.dismiss(toast.id)" aria-label="Dismiss notification">
            &times;
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      top: 1rem;
      right: 1rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      z-index: 1000;
      max-width: 24rem;
    }
    .toast {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border-radius: 0.5rem;
      color: #fff;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      font-size: 0.9rem;
    }
    .toast--success { background: #1e7e34; }
    .toast--error { background: #c62828; }
    .toast--info { background: #1565c0; }
    .toast__close {
      background: none;
      border: none;
      color: inherit;
      font-size: 1.1rem;
      cursor: pointer;
      line-height: 1;
    }
  `],
})
export class ToastContainerComponent {
  protected readonly toastService = inject(ToastService);
}
