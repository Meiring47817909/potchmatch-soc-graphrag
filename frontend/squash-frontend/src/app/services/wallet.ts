import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { DepositApiRequest, WalletApiResponse } from '../models/api';

// Talks to PaymentService's PaymentsController wallet endpoints via the
// Gateway - the same microservice that already holds/releases/refunds escrow
// for challenges and coaching sessions, so a top-up here and a challenge
// payout both land in the same balance.
@Service()
export class WalletApi {
  private readonly http = inject(HttpClient);

  getBalance(): Observable<WalletApiResponse> {
    return this.http.get<WalletApiResponse>(`${API_BASE_URL}/api/payments/wallet`);
  }

  deposit(request: DepositApiRequest): Observable<WalletApiResponse> {
    return this.http.post<WalletApiResponse>(`${API_BASE_URL}/api/payments/wallet/deposit`, request);
  }
}
