import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { ChangePasswordApiRequest, PlayerApiResponse, UpdatePlayerApiRequest } from '../models/api';

// Talks to IdentityService's PlayersController via the Gateway.
@Service()
export class PlayersApi {
  private readonly http = inject(HttpClient);

  getAll(): Observable<PlayerApiResponse[]> {
    return this.http.get<PlayerApiResponse[]>(`${API_BASE_URL}/api/players`);
  }

  getById(id: string): Observable<PlayerApiResponse> {
    return this.http.get<PlayerApiResponse>(`${API_BASE_URL}/api/players/${id}`);
  }

  update(id: string, request: UpdatePlayerApiRequest): Observable<PlayerApiResponse> {
    return this.http.put<PlayerApiResponse>(`${API_BASE_URL}/api/players/${id}`, request);
  }

  changePassword(id: string, request: ChangePasswordApiRequest): Observable<void> {
    return this.http.put<void>(`${API_BASE_URL}/api/players/${id}/password`, request);
  }
}
