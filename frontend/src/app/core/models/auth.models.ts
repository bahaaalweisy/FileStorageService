export type UserRole = 'user' | 'admin';

export interface DemoIdentity {
  userId: string;
  displayName: string;
  role: UserRole;
}

export interface MockTokenResponse {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  role: UserRole;
  displayName: string;
}
