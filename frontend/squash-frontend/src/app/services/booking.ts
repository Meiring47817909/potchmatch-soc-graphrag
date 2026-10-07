import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../core/api-config';
import { BookingApiResponse } from '../models/api';

export interface CreateBookingRequest {
  clubId: string;
  feeZAR: number;
  // ISO date string (e.g. from a date input's value) - the backend rejects
  // anything in the past (see BookingController.Create).
  scheduledAt: string;
  // Explicit type choice - the backend rejects true here unless the caller is
  // actually a Coach (see BookingService's BookingController.Create).
  isCoachingSession: boolean;
}

// Talks to BookingService's BookingController via the Gateway, at the
// "/api/bookings" route (booking covers both a plain Challenge and a
// CoachingSession - see IsCoachingSession above). Accept() hits the endpoint
// that internally runs the Saga (holds funds in PaymentService, then
// atomically commits or compensates) - see BookingController.Accept on the backend.
@Service()
export class BookingApi {
  private readonly http = inject(HttpClient);

  getAll(clubId?: string): Observable<BookingApiResponse[]> {
    const url = clubId
      ? `${API_BASE_URL}/api/bookings?clubId=${encodeURIComponent(clubId)}`
      : `${API_BASE_URL}/api/bookings`;
    return this.http.get<BookingApiResponse[]>(url);
  }

  create(request: CreateBookingRequest): Observable<BookingApiResponse> {
    return this.http.post<BookingApiResponse>(`${API_BASE_URL}/api/bookings`, request);
  }

  accept(id: string): Observable<BookingApiResponse> {
    return this.http.put<BookingApiResponse>(`${API_BASE_URL}/api/bookings/${id}/accept`, null);
  }
}
