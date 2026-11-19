import { HttpClient } from '@angular/common/http';
import { Service, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { LoginRequest, LoginResponse, RegisterRequest, RegisterResponse } from '../interfaces/auth_dtos';

const TOKEN_KEY = 'cbot_access_token';
const USER_KEY = 'cbot_user_name';

@Service()
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly tokenState = signal<string | null>(this.readStorage(TOKEN_KEY));
  private readonly userState = signal<string | null>(this.readStorage(USER_KEY));
  private readonly expiresAt = signal<number | null>(this.readExpiry(this.tokenState()));
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;
  readonly isLoggedIn = computed(() => !!this.tokenState() && (this.expiresAt() ?? 0) > Date.now());
  readonly userName = this.userState.asReadonly();
  private readonly authUrl = `${environment.apiUrl}/api/auth`;

  constructor() {
    if (!this.isLoggedIn()) {
      this.clearSession();
    } else {
      this.scheduleExpiry();
    }
  }

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.authUrl}/login`, request).pipe(tap((response) => this.setSession(response)));
  }

  register(request: RegisterRequest): Observable<RegisterResponse> {
    return this.http.post<RegisterResponse>(`${this.authUrl}/register`, request);
  }

  token(): string | null { return this.isLoggedIn() ? this.tokenState() : null; }

  logout(redirect = true): void {
    this.clearSession();
    if (redirect) void this.router.navigate(['/login']);
  }

  private clearSession(): void {
    this.tokenState.set(null);
    this.userState.set(null);
    this.expiresAt.set(null);
    if (this.expiryTimer) clearTimeout(this.expiryTimer);
    if (typeof localStorage !== 'undefined') {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(USER_KEY);
    }
  }

  private setSession(response: LoginResponse): void {
    this.tokenState.set(response.accessToken);
    this.userState.set(response.userName);
    this.expiresAt.set(this.readExpiry(response.accessToken));
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(TOKEN_KEY, response.accessToken);
      localStorage.setItem(USER_KEY, response.userName);
    }
    this.scheduleExpiry();
  }

  private readExpiry(token: string | null): number | null {
    try {
      const payload = token?.split('.')[1];
      if (!payload) return null;
      const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '='))) as { exp?: unknown };
      return typeof claims.exp === 'number' ? claims.exp * 1000 : null;
    } catch {
      return null;
    }
  }

  private scheduleExpiry(): void {
    if (this.expiryTimer) clearTimeout(this.expiryTimer);
    const expiry = this.expiresAt();
    if (expiry === null) {
      this.clearSession();
      return;
    }
    this.expiryTimer = setTimeout(() => this.logout(), Math.max(0, expiry - Date.now()));
  }

  private readStorage(key: string): string | null {
    return typeof localStorage === 'undefined' ? null : localStorage.getItem(key);
  }
}
