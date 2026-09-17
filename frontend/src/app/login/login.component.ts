import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { UserRole } from '../core/models/auth.models';
import { ToastService } from '../shared/toast/toast.service';

@Component({
  selector: 'app-login',
  standalone: true,
  template: `
    <div class="login-card">
      <h1>File Storage Service</h1>
      <p class="subtitle">Demo login — pick a role to obtain a mock JWT issued by the backend.</p>

      <div class="role-options">
        <button type="button" class="role-button" [disabled]="busy()" (click)="loginAs('user')">
          <strong>Sign in as User</strong>
          <span>Upload files, manage your own files, soft-delete your own files.</span>
        </button>

        <button type="button" class="role-button" [disabled]="busy()" (click)="loginAs('admin')">
          <strong>Sign in as Admin</strong>
          <span>Access every file, hard-delete already soft-deleted files.</span>
        </button>
      </div>

      <p class="note">
        This is a development-only mock login. No password is required or stored; the backend signs a
        short-lived JWT for a fixed demo identity. See README for details.
      </p>
    </div>
  `,
  styles: [`
    :host {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      padding: 1rem;
      background: #f4f6f8;
    }
    .login-card {
      background: #fff;
      border-radius: 0.75rem;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
      padding: 2.5rem;
      max-width: 30rem;
      width: 100%;
    }
    h1 { margin: 0 0 0.5rem; font-size: 1.5rem; }
    .subtitle { color: #555; margin: 0 0 1.5rem; }
    .role-options { display: flex; flex-direction: column; gap: 0.75rem; }
    .role-button {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 0.25rem;
      padding: 1rem;
      border: 1px solid #d0d7de;
      border-radius: 0.5rem;
      background: #fafbfc;
      cursor: pointer;
      text-align: left;
      font-size: 0.95rem;
    }
    .role-button:hover:not(:disabled) { border-color: #1565c0; background: #eef4fc; }
    .role-button:focus-visible { outline: 3px solid #1565c0; outline-offset: 2px; }
    .role-button:disabled { opacity: 0.6; cursor: progress; }
    .role-button span { color: #666; font-size: 0.85rem; }
    .note { margin-top: 1.5rem; font-size: 0.8rem; color: #888; }
  `],
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  protected readonly busy = signal(false);

  async loginAs(role: UserRole): Promise<void> {
    this.busy.set(true);
    try {
      await this.auth.login(role);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/files';
      await this.router.navigateByUrl(returnUrl);
      this.toast.success(`Signed in as ${role}.`);
    } catch {
      this.toast.error('Could not sign in. Is the API reachable?');
    } finally {
      this.busy.set(false);
    }
  }
}
