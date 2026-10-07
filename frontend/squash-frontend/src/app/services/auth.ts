import { Service, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { AuthResponse, LoginRequest, RegisterRequest } from '../models/auth';

const TOKEN_STORAGE_KEY = 'squash_platform_token';
const PLAYER_STORAGE_KEY = 'squash_platform_player';

export interface AuthenticatedPlayer {
  playerId: string;
  name: string;
  surname: string;
  role: string;
  clubId: string;
}

// Handles login/register against IdentityService (via the Gateway) and keeps
// track of the current session. The token and player info are kept in
// localStorage so a page refresh doesn't log the user out - this is a normal
// browser app running on the user's own machine, not a hosted artifact, so
// localStorage is the right tool here.
@Service()
export class Auth {
  // @Service classes in this codebase can't use constructor DI (NG2028) -
  // the `inject()` function is the required style here instead.
  private readonly http = inject(HttpClient);

  readonly currentPlayer = signal<AuthenticatedPlayer | null>(this.loadStoredPlayer());

  get token(): string | null {
    return localStorage.getItem(TOKEN_STORAGE_KEY);
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE_URL}/api/auth/login`, request)
      .pipe(tap((response) => this.storeSession(response)));
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE_URL}/api/auth/register`, request)
      .pipe(tap((response) => this.storeSession(response)));
  }

  logout(): void {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    localStorage.removeItem(PLAYER_STORAGE_KEY);
    this.currentPlayer.set(null);
  }

  // Called by the Dashboard after a successful profile save, so the session's
  // name/club (used elsewhere - the top bar, the host-at-own-club check) stay
  // in sync without forcing the player to log out and back in.
  updateStoredProfile(update: Partial<Pick<AuthenticatedPlayer, 'name' | 'surname' | 'clubId'>>): void {
    const current = this.currentPlayer();
    if (!current) return;

    const updated: AuthenticatedPlayer = { ...current, ...update };
    localStorage.setItem(PLAYER_STORAGE_KEY, JSON.stringify(updated));
    this.currentPlayer.set(updated);
  }

  private storeSession(response: AuthResponse): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, response.token);

    const player: AuthenticatedPlayer = {
      playerId: response.playerId,
      name: response.name,
      surname: response.surname,
      role: response.role,
      clubId: response.clubId,
    };

    localStorage.setItem(PLAYER_STORAGE_KEY, JSON.stringify(player));
    this.currentPlayer.set(player);
  }

  private loadStoredPlayer(): AuthenticatedPlayer | null {
    const raw = localStorage.getItem(PLAYER_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as AuthenticatedPlayer) : null;
  }
}
