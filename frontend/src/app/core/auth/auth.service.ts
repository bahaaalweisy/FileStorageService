import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MockTokenResponse, UserRole } from '../models/auth.models';

interface StoredSession {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  role: UserRole;
  displayName: string;
}

const SESSION_STORAGE_KEY = 'fileStorage.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly session = signal<StoredSession | null>(this.readSession());

  readonly displayName = computed(() => this.session()?.displayName ?? null);
  readonly role = computed(() => this.session()?.role ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.session()?.role === 'admin');

  getToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  async login(role: UserRole): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<MockTokenResponse>(`${environment.apiBaseUrl}/auth/mock-token`, { role }),
    );

    const stored: StoredSession = {
      accessToken: response.accessToken,
      expiresAtUtc: response.expiresAtUtc,
      userId: response.userId,
      role: response.role,
      displayName: response.displayName,
    };

    this.session.set(stored);
    sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(stored));
  }

  logout(): void {
    this.session.set(null);
    sessionStorage.removeItem(SESSION_STORAGE_KEY);
  }

  private readSession(): StoredSession | null {
    const raw = sessionStorage.getItem(SESSION_STORAGE_KEY);
    if (!raw) {
      return null;
    }

    try {
      const parsed = JSON.parse(raw) as StoredSession;
      if (new Date(parsed.expiresAtUtc).getTime() <= Date.now()) {
        sessionStorage.removeItem(SESSION_STORAGE_KEY);
        return null;
      }
      return parsed;
    } catch {
      return null;
    }
  }
}
